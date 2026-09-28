using System.CommandLine;
using System.Text.Json;

namespace DevBuddy.Client;

/// <summary>
/// Every <c>devbuddy</c> command (ADR-0015). Exit codes: 0 done, 1 refused or failed, and for
/// <c>mcp-headers</c> 2 for an unregistered folder, 3 for a URL that is not the registered server,
/// 4 for a missing token.
/// </summary>
internal static class Commands
{
    public const int Done = 0;
    public const int Failed = 1;
    public const int NotRegistered = 2;
    public const int WrongServer = 3;
    public const int NoToken = 4;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Response files off, as the console has them: a path beginning with <c>@</c> would
    /// otherwise be read as a file of further arguments.
    /// </summary>
    public static ParserConfiguration Parsing { get; } = new() { ResponseFileTokenReplacer = null };

    /// <summary>
    /// A failure is reported by <see cref="RunAsync"/> as one sentence, not a stack trace: the
    /// person reading it, or the assistant, needs what went wrong and not where in the client.
    /// </summary>
    private static InvocationConfiguration Invocation { get; } = new() { EnableDefaultExceptionHandler = false };

    public static async Task<int> RunAsync(ClientContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return await Build(context).Parse(args, Parsing).InvokeAsync(Invocation);
        }
        catch (InvalidOperationException exception)
        {
            context.Error.WriteLine($"DevBuddy: {exception.Message}");
            return Failed;
        }
    }

    public static RootCommand Build(ClientContext context)
    {
        RootCommand root = new(
            "DevBuddy client. Keeps your machine token per checkout and hands it to Claude Code and "
            + "Codex when they connect to DevBuddy's MCP server.");

        root.Add(Register(context));
        root.Add(List(context));
        root.Add(Show(context));
        root.Add(Update(context));
        root.Add(Unregister(context));
        root.Add(Token(context));
        root.Add(Doctor(context));
        root.Add(McpHeaders(context));

        return root;
    }

    private static Argument<string?> PathArgument() => new("path")
    {
        Description = "The checkout. Defaults to the git repository holding the current folder.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private static Command Register(ClientContext context)
    {
        Argument<string?> path = PathArgument();
        Option<string> server = new("--server") { Description = "The DevBuddy server, e.g. https://192.168.1.160:5010.", Required = true };
        Option<Guid> workspace = new("--workspace") { Description = "The workspace your token is for.", Required = true };
        Option<Guid?> project = new("--project") { Description = "The project assistants should work in by default." };

        Command command = new("register", "Registers a checkout: which server and workspace it belongs to.")
        {
            path, server, workspace, project,
        };

        command.SetAction(async (result, cancellationToken) =>
        {
            string? root = CheckoutRoot(context, result.GetValue(path));

            if (root is null)
            {
                return Failed;
            }

            if (!ServerOrigin.TryParse(result.GetRequiredValue(server), out string origin, out string problem))
            {
                context.Error.WriteLine(problem);
                return Failed;
            }

            Guid workspaceId = result.GetRequiredValue(workspace);

            if (!await EnsureTokenAsync(context, origin, workspaceId, cancellationToken))
            {
                return Failed;
            }

            var registry = Registry.Load(context.Home);
            var checkout = new Checkout(root, origin, workspaceId, result.GetValue(project));
            registry.Put(checkout);
            registry.Save();

            context.Out.WriteLine($"Registered {root}");
            Describe(context, checkout);
            return Done;
        });

        return command;
    }

    private static Command List(ClientContext context)
    {
        Command command = new("list", "Lists every registered checkout.");

        command.SetAction(_ =>
        {
            var registry = Registry.Load(context.Home);

            if (registry.All.Count == 0)
            {
                context.Out.WriteLine("No checkout is registered. Run `devbuddy register` in one.");
                return Done;
            }

            context.Out.WriteLine(
                "Registered checkouts. These say which server and workspace a folder's token is for; "
                + "DevBuddy's projects and knowledge come from its MCP tools, such as list_projects.");

            foreach (Checkout checkout in registry.All)
            {
                string missing = Directory.Exists(checkout.Path) ? string.Empty : "  (folder missing)";
                context.Out.WriteLine($"{checkout.Path}{missing}");
                context.Out.WriteLine($"    {checkout.Server}  workspace {checkout.Workspace:D}"
                    + (checkout.Project is { } id ? $"  project {id:D}" : string.Empty));
            }

            return Done;
        });

        return command;
    }

    private static Command Show(ClientContext context)
    {
        Argument<string?> path = PathArgument();
        Option<bool> json = new("--json")
        {
            Description = "Machine-readable, for an assistant to read its workspace and project from. Never reads the token.",
        };

        Command command = new("show", "Shows what the checkout holding a folder is registered to.") { path, json };

        command.SetAction(result =>
        {
            string folder = Path.GetFullPath(result.GetValue(path) ?? context.WorkingDirectory);
            Checkout? checkout = Registry.Load(context.Home).Find(folder);

            if (checkout is null)
            {
                context.Error.WriteLine($"{folder} is not in a registered checkout. Run `devbuddy register` there.");
                return NotRegistered;
            }

            // What an assistant asks for, from inside its sandbox, where the credential store may be
            // out of reach. It needs the workspace and project, never the token, so the store is
            // not touched: Codex's sandbox refused the keychain, and this threw, on 2026-09-28.
            if (result.GetValue(json))
            {
                context.Out.WriteLine(JsonSerializer.Serialize(new
                {
                    path = checkout.Path,
                    server = checkout.Server,
                    mcpUrl = ServerOrigin.McpEndpoint(checkout.Server).ToString(),
                    workspaceId = checkout.Workspace,
                    projectId = checkout.Project,
                    note = "This is a registration, not DevBuddy content. Ask DevBuddy through its MCP tools.",
                }, Json));

                return Done;
            }

            Describe(context, checkout);
            context.Out.WriteLine(TryReadToken(context, checkout, out string? problem) is not null
                ? $"Token:     stored in {context.Store.Description}"
                : problem is null
                    ? "Token:     none stored. Run `devbuddy token set`."
                    : $"Token:     could not be checked here. {problem}");
            return Done;
        });

        return command;
    }

    private static Command Update(ClientContext context)
    {
        Argument<string?> path = PathArgument();
        Option<string?> server = new("--server") { Description = "A new server." };
        Option<Guid?> workspace = new("--workspace") { Description = "A new workspace." };
        Option<Guid?> project = new("--project") { Description = "A new default project." };
        Option<bool> clearProject = new("--clear-project") { Description = "Removes the default project." };

        Command command = new("update", "Changes a registered checkout.") { path, server, workspace, project, clearProject };

        command.SetAction(async (result, cancellationToken) =>
        {
            var registry = Registry.Load(context.Home);
            Checkout? existing = Registered(context, registry, result.GetValue(path));

            if (existing is null)
            {
                return NotRegistered;
            }

            string origin = existing.Server;

            if (result.GetValue(server) is { } newServer
                && !ServerOrigin.TryParse(newServer, out origin, out string problem))
            {
                context.Error.WriteLine(problem);
                return Failed;
            }

            Guid workspaceId = result.GetValue(workspace) ?? existing.Workspace;

            if ((origin != existing.Server || workspaceId != existing.Workspace)
                && !await EnsureTokenAsync(context, origin, workspaceId, cancellationToken))
            {
                return Failed;
            }

            Guid? projectId = result.GetValue(clearProject) ? null : result.GetValue(project) ?? existing.Project;
            var updated = new Checkout(existing.Path, origin, workspaceId, projectId);

            registry.Put(updated);
            registry.Save();

            context.Out.WriteLine($"Updated {updated.Path}");
            Describe(context, updated);
            return Done;
        });

        return command;
    }

    private static Command Unregister(ClientContext context)
    {
        Argument<string?> path = PathArgument();
        Command command = new("unregister", "Forgets a checkout. Its workspace's token stays, for other checkouts.") { path };

        command.SetAction(result =>
        {
            var registry = Registry.Load(context.Home);
            Checkout? existing = Registered(context, registry, result.GetValue(path));

            if (existing is null)
            {
                return NotRegistered;
            }

            registry.Remove(existing.Path);
            registry.Save();

            context.Out.WriteLine($"Unregistered {existing.Path}.");

            if (registry.UsingToken(existing.Server, existing.Workspace).Count == 0)
            {
                context.Out.WriteLine(
                    "No other checkout uses its token. `devbuddy token remove "
                    + $"--server {existing.Server} --workspace {existing.Workspace:D}` removes it.");
            }

            return Done;
        });

        return command;
    }

    private static Command Token(ClientContext context)
    {
        Command token = new("token", "Stores or removes the token for a server and workspace.");

        Option<string> setServer = new("--server") { Description = "The DevBuddy server.", Required = true };
        Option<Guid> setWorkspace = new("--workspace") { Description = "The workspace the token is for.", Required = true };
        Command set = new("set", "Stores a new token, after the server accepts it. Asks for it; never takes it as an argument.")
        {
            setServer, setWorkspace,
        };

        set.SetAction(async (result, cancellationToken) =>
        {
            if (!ServerOrigin.TryParse(result.GetRequiredValue(setServer), out string origin, out string problem))
            {
                context.Error.WriteLine(problem);
                return Failed;
            }

            return await AskAndStoreAsync(context, origin, result.GetRequiredValue(setWorkspace), cancellationToken)
                ? Done
                : Failed;
        });

        Option<string> removeServer = new("--server") { Description = "The DevBuddy server.", Required = true };
        Option<Guid> removeWorkspace = new("--workspace") { Description = "The workspace the token is for.", Required = true };
        Option<bool> force = new("--force") { Description = "Removes it even though registered checkouts still use it." };
        Command remove = new("remove", "Removes a stored token.") { removeServer, removeWorkspace, force };

        remove.SetAction(result =>
        {
            if (!ServerOrigin.TryParse(result.GetRequiredValue(removeServer), out string origin, out string problem))
            {
                context.Error.WriteLine(problem);
                return Failed;
            }

            Guid workspaceId = result.GetRequiredValue(removeWorkspace);
            IReadOnlyList<Checkout> users = Registry.Load(context.Home).UsingToken(origin, workspaceId);

            if (users.Count > 0 && !result.GetValue(force))
            {
                context.Error.WriteLine("These checkouts still use that token, and would stop working:");

                foreach (Checkout checkout in users)
                {
                    context.Error.WriteLine($"    {checkout.Path}");
                }

                context.Error.WriteLine("Unregister them first, or pass --force.");
                return Failed;
            }

            bool removed = context.Store.Delete(CredentialStores.KeyFor(origin, workspaceId));
            context.Out.WriteLine(removed ? "Removed." : "No token was stored for that server and workspace.");
            context.Out.WriteLine("Revoke it under Plugin access too, if it should stop working everywhere.");
            return Done;
        });

        token.Add(set);
        token.Add(remove);
        return token;
    }

    private static Command Doctor(ClientContext context)
    {
        Argument<string?> path = PathArgument();
        Command command = new("doctor", "Checks, end to end, that assistants in this checkout can reach DevBuddy.") { path };

        command.SetAction(async (result, cancellationToken) =>
        {
            string folder = Path.GetFullPath(result.GetValue(path) ?? context.WorkingDirectory);
            Checkout? checkout = Registry.Load(context.Home).Find(folder);

            if (checkout is null)
            {
                context.Out.WriteLine($"[fail] {folder} is not in a registered checkout. Run `devbuddy register` there.");
                return NotRegistered;
            }

            context.Out.WriteLine($"[ok]   registered: {checkout.Path}");
            context.Out.WriteLine($"       server {checkout.Server}, workspace {checkout.Workspace:D}");

            string? token = TryReadToken(context, checkout, out string? problem);

            if (token is null)
            {
                context.Out.WriteLine(problem is null
                    ? "[fail] no token stored for that server and workspace. Run `devbuddy token set`."
                    : $"[fail] the credential store could not be read. {problem}");
                return NoToken;
            }

            context.Out.WriteLine($"[ok]   token stored in {context.Store.Description}");

            CheckResult check = await new ServerCheck(context.Http)
                .VerifyAsync(checkout.Server, token, checkout.Workspace, cancellationToken);

            context.Out.WriteLine($"{(check.Succeeded ? "[ok]  " : "[fail]")} {check.Explain(checkout.Server)}");

            if (check.Succeeded)
            {
                context.Out.WriteLine($"       Assistants connect to {ServerOrigin.McpEndpoint(checkout.Server)}.");
            }

            return check.Succeeded ? Done : Failed;
        });

        return command;
    }

    /// <summary>
    /// The header helper Claude Code and Codex run when they connect. Standard output is the
    /// protocol: a JSON object of headers, or nothing at all.
    /// </summary>
    private static Command McpHeaders(ClientContext context)
    {
        Option<string?> dir = new("--dir") { Description = "The folder the assistant was started in. Defaults to the current folder." };
        Option<string?> url = new("--url") { Description = "The MCP URL being connected to. Defaults to CLAUDE_CODE_MCP_SERVER_URL." };

        Command command = new("mcp-headers", "Prints the Authorization header for an assistant's MCP connection.") { dir, url };

        command.SetAction(result =>
        {
            string folder = Path.GetFullPath(
                result.GetValue(dir) is { Length: > 0 } given ? given : context.WorkingDirectory);
            string? target = result.GetValue(url) is { Length: > 0 } explicitUrl
                ? explicitUrl
                : context.Environment("CLAUDE_CODE_MCP_SERVER_URL");

            Checkout? checkout = Registry.Load(context.Home).Find(folder);

            // Nothing is sent rather than a guess: a token chosen for the wrong checkout would act
            // in a workspace nobody meant this session to reach.
            if (checkout is null)
            {
                context.Error.WriteLine(
                    $"DevBuddy: {folder} is not in a registered checkout, so no token was sent. "
                    + "Run `devbuddy register` in it.");
                return NotRegistered;
            }

            // The one check that keeps the token from leaving for anywhere else: a configuration
            // that names another server, by mistake or not, gets nothing.
            if (!ServerOrigin.IsOn(target, checkout.Server))
            {
                context.Error.WriteLine(
                    $"DevBuddy: this checkout is registered to {checkout.Server}, but the assistant is "
                    + $"connecting to '{target ?? "(no URL given)"}'. No token was sent.");
                return WrongServer;
            }

            string? token = TryReadToken(context, checkout, out string? problem);

            if (token is null)
            {
                context.Error.WriteLine(problem is null
                    ? $"DevBuddy: no token is stored for {checkout.Server}, workspace {checkout.Workspace:D}. "
                        + "Run `devbuddy token set`."
                    : $"DevBuddy: the credential store could not be read, so no token was sent. {problem}");
                return NoToken;
            }

            context.Out.WriteLine(JsonSerializer.Serialize(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = $"Bearer {token}" }));
            return Done;
        });

        return command;
    }

    /// <summary>
    /// The git repository holding a folder, or the folder itself when it is already registered.
    /// A folder that is neither is refused rather than registered as a guess.
    /// </summary>
    private static string? CheckoutRoot(ClientContext context, string? path)
    {
        string folder = Path.GetFullPath(path ?? context.WorkingDirectory);

        if (!Directory.Exists(folder))
        {
            context.Error.WriteLine($"{folder} does not exist.");
            return null;
        }

        if (Folders.GitRoot(folder) is { } root)
        {
            return root;
        }

        context.Error.WriteLine($"{folder} is not in a git repository. Register a checkout.");
        return null;
    }

    private static Checkout? Registered(ClientContext context, Registry registry, string? path)
    {
        string folder = Path.GetFullPath(path ?? context.WorkingDirectory);
        Checkout? checkout = registry.Exact(folder) ?? registry.Find(folder);

        if (checkout is null)
        {
            context.Error.WriteLine($"{folder} is not in a registered checkout.");
        }

        return checkout;
    }

    /// <summary>
    /// A stored token that still works, or a new one asked for, checked and stored. A stored one
    /// the server now refuses is reported rather than silently replaced.
    /// </summary>
    private static async Task<bool> EnsureTokenAsync(
        ClientContext context, string origin, Guid workspace, CancellationToken cancellationToken)
    {
        string? stored = context.Store.Read(CredentialStores.KeyFor(origin, workspace));

        if (stored is null)
        {
            return await AskAndStoreAsync(context, origin, workspace, cancellationToken);
        }

        CheckResult check = await new ServerCheck(context.Http).VerifyAsync(origin, stored, workspace, cancellationToken);

        if (!check.Succeeded)
        {
            context.Error.WriteLine($"The stored token for this workspace does not work. {check.Explain(origin)}");
        }

        return check.Succeeded;
    }

    private static async Task<bool> AskAndStoreAsync(
        ClientContext context, string origin, Guid workspace, CancellationToken cancellationToken)
    {
        string? token = context.ReadToken(
            $"Machine token for {origin}, workspace {workspace:D} (from Plugin access; not shown): ");

        if (!TokenFormat.IsValid(token))
        {
            context.Error.WriteLine(
                $"That is not a machine token: one is exactly {TokenFormat.Length} characters of letters, "
                + "digits, '-' and '_'. Copy only the token.");
            return false;
        }

        CheckResult check = await new ServerCheck(context.Http).VerifyAsync(origin, token!, workspace, cancellationToken);

        if (!check.Succeeded)
        {
            context.Error.WriteLine($"Not stored. {check.Explain(origin)}");
            return false;
        }

        context.Store.Write(CredentialStores.KeyFor(origin, workspace), token!);
        context.Out.WriteLine($"Token stored in {context.Store.Description}.");
        return true;
    }

    /// <summary>
    /// The stored token, or null with the reason when the store cannot be reached: a locked
    /// keychain, or an assistant's sandbox that does not let a command reach it at all.
    /// </summary>
    private static string? TryReadToken(ClientContext context, Checkout checkout, out string? problem)
    {
        problem = null;

        try
        {
            return context.Store.Read(CredentialStores.KeyFor(checkout.Server, checkout.Workspace));
        }
        catch (InvalidOperationException exception)
        {
            problem = exception.Message;
            return null;
        }
    }

    private static void Describe(ClientContext context, Checkout checkout)
    {
        context.Out.WriteLine($"Server:    {checkout.Server}");
        context.Out.WriteLine($"Workspace: {checkout.Workspace:D}");
        context.Out.WriteLine($"Project:   {(checkout.Project is { } id ? id.ToString("D") : "(none)")}");
    }
}
