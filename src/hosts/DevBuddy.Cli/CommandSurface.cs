using System.CommandLine;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Dispatch;

namespace DevBuddy.Cli;

/// <summary>
/// Every console command, in one place.
/// <para>
/// The named commands are conveniences over <c>run</c>: they build the JSON body an operation
/// expects and then take the identical path. That is why there is no per-command handler doing
/// its own work — a console command that reached past the dispatcher would be a fourth place for
/// authorization, redaction, and audit to be got wrong.
/// </para>
/// </summary>
internal static class CommandSurface
{
    private static readonly Option<string> Actor =
        new("--actor") { Description = "The user identifier to act as. Falls back to DEVBUDDY_ACTOR." };

    private static readonly Option<Guid> Workspace =
        new("--workspace") { Description = "Workspace identifier.", Required = true };

    private static readonly Option<Guid> Project =
        new("--project") { Description = "Project identifier.", Required = true };

    /// <summary>
    /// How the console parses a command line, with response files switched off.
    /// <para>
    /// System.CommandLine treats any token beginning with <c>@</c> as a file to read further
    /// arguments from, which quietly turns a password like <c>@Aa123456</c> into "read arguments
    /// from a file named Aa123456" and reports "Response file not found" — a message naming
    /// neither the option nor the reason. A value beginning with <c>@</c> is a perfectly ordinary
    /// password, nothing here takes arguments from a file, and the one case that does not fail is
    /// a working directory holding a file of that name, whose contents would then be parsed as
    /// arguments.
    /// </para>
    /// <para>
    /// Exposed rather than applied at the call site so a test can parse through the same
    /// configuration the console uses.
    /// </para>
    /// </summary>
    public static ParserConfiguration Parsing { get; } = new() { ResponseFileTokenReplacer = null };

    public static RootCommand Build()
    {
        RootCommand root = new(
            "DevBuddy console. Administrative and local operations over the same pipeline the API "
            + "and the MCP server use.");

        root.Add(Actor);
        Actor.Recursive = true;

        root.Add(Migrate());
        root.Add(Bootstrap());
        root.Add(Operations());
        root.Add(Run());
        root.Add(Health());
        root.Add(Record());
        root.Add(Export());
        root.Add(Backup());
        root.Add(Restore());
        root.Add(Retention());
        root.Add(Sync());
        root.Add(Reindex());

        return root;
    }

    /// <summary>
    /// Applies migrations. Outside the pipeline because it runs before the schema the pipeline
    /// reads even exists.
    /// </summary>
    private static Command Migrate()
    {
        Command command = new("migrate", "Applies pending database migrations.");

        command.SetAction((_, cancellationToken) =>
            Runner.MigrateAsync(cancellationToken));

        return command;
    }

    /// <summary>
    /// Creates the first workspace and the first administrator, once, on an empty database.
    /// </summary>
    private static Command Bootstrap()
    {
        Option<string> workspaceName =
            new("--workspace-name") { Description = "Name for the first workspace.", Required = true };

        Option<string> email =
            new("--email") { Description = "Administrator email address.", Required = true };

        Option<string> password =
            new("--password") { Description = "Administrator password.", Required = true };

        Option<string?> projectName =
            new("--project-name") { Description = "Optional first project." };

        Command command = new(
            "bootstrap",
            "Creates the first workspace and administrator. Refuses once a workspace exists.");

        command.Add(workspaceName);
        command.Add(email);
        command.Add(password);
        command.Add(projectName);

        command.SetAction((result, cancellationToken) => Runner.BootstrapAsync(
            result.GetRequiredValue(workspaceName),
            result.GetRequiredValue(email),
            result.GetRequiredValue(password),
            result.GetValue(projectName),
            cancellationToken));

        return command;
    }

    private static Command Operations()
    {
        Option<bool> aiOnly =
            new("--ai") { Description = "List only the operations exposed to AI." };

        Command command = new("operations", "Lists every operation this deployment can perform.");
        command.Add(aiOnly);

        command.SetAction(result => Runner.ListOperations(result.GetValue(aiOnly)));

        return command;
    }

    /// <summary>The general form. Everything below is this with the body filled in.</summary>
    private static Command Run()
    {
        Argument<string> name = new("name") { Description = "The operation name, as `operations` lists it." };

        Option<string?> arguments =
            new("--arguments") { Description = "Operation arguments, as a JSON object." };

        Option<FileInfo?> argumentsFile =
            new("--arguments-file") { Description = "A file holding the JSON arguments." };

        Command command = new("run", "Runs one operation by name.");
        command.Add(name);
        command.Add(arguments);
        command.Add(argumentsFile);

        command.SetAction(async (result, cancellationToken) =>
        {
            FileInfo? file = result.GetValue(argumentsFile);

            string body = file is not null
                ? await File.ReadAllTextAsync(file.FullName, cancellationToken)
                : result.GetValue(arguments) ?? "{}";

            return await Runner.InvokeAsync(
                result.GetRequiredValue(name), body, result.GetValue(Actor), cancellationToken);
        });

        return command;
    }

    private static Command Health()
    {
        Command command = new("health", "Reports component health. Names components, never credentials.");
        command.Add(Workspace);

        command.SetAction((result, cancellationToken) => Runner.InvokeAsync(
            UseCaseCatalog.CheckSystemHealth.Name,
            Body(("workspaceId", result.GetRequiredValue(Workspace))),
            result.GetValue(Actor),
            cancellationToken));

        return command;
    }

    private static Command Record()
    {
        Argument<Guid> recordId = new("record-id") { Description = "The knowledge record identifier." };

        Option<int?> revision =
            new("--revision") { Description = "A specific revision number. Defaults to the published one." };

        Command command = new("record", "Reads one knowledge record.");
        command.Add(recordId);
        command.Add(Workspace);
        command.Add(Project);
        command.Add(revision);

        command.SetAction((result, cancellationToken) => Runner.InvokeAsync(
            UseCaseCatalog.GetRecord.Name,
            Scoped(
                result.GetRequiredValue(Workspace),
                result.GetRequiredValue(Project),
                ("recordId", result.GetRequiredValue(recordId)),
                ("revisionNumber", result.GetValue(revision))),
            result.GetValue(Actor),
            cancellationToken));

        return command;
    }

    private static Command Export()
    {
        Command command = new("export", "Exports one project.");
        command.Add(Workspace);
        command.Add(Project);

        command.SetAction((result, cancellationToken) => Runner.InvokeAsync(
            UseCaseCatalog.ExportProject.Name,
            Scoped(result.GetRequiredValue(Workspace), result.GetRequiredValue(Project)),
            result.GetValue(Actor),
            cancellationToken));

        return command;
    }

    private static Command Backup()
    {
        Command command = new("backup", "Takes a backup of the database and the evidence store.");
        command.Add(Workspace);

        command.SetAction((result, cancellationToken) => Runner.InvokeAsync(
            UseCaseCatalog.BackupSystem.Name,
            Body(("workspaceId", result.GetRequiredValue(Workspace))),
            result.GetValue(Actor),
            cancellationToken));

        return command;
    }

    /// <summary>
    /// Restores from a backup.
    /// <para>
    /// A command rather than an operation, and the reason is structural: every operation is
    /// authorised against a membership, and a restore from total loss runs against a database with
    /// no memberships in it. There is no caller to authorise, so the pipeline correctly refuses —
    /// which makes a restore operation one that can never succeed. It sits beside migrate and
    /// bootstrap, outside the pipeline, for exactly the same reason they do.
    /// </para>
    /// </summary>
    private static Command Restore()
    {
        Option<string> reference =
            new("--reference") { Description = "The backup reference to restore.", Required = true };

        Command command = new("restore", "Restores from a backup, into an empty installation.");
        command.Add(reference);

        command.SetAction((result, cancellationToken) =>
            Runner.RestoreAsync(result.GetRequiredValue(reference), cancellationToken));

        return command;
    }

    /// <summary>
    /// Applies the retention schedule: outside the pipeline for the same reason <c>restore</c> is.
    /// A sweep spans every workspace and project, so there is no single caller to authorise it
    /// against. Run it on whatever schedule the operator's own cron or task scheduler provides —
    /// this system starts no scheduler of its own (SB-27).
    /// </summary>
    /// <summary>
    /// Applies the retention schedule, once or on an interval.
    /// <para>
    /// The interval is what lets the shipped stack schedule its own sweep. The images are
    /// chiseled — no shell, no <c>cron</c> — so the alternative was a shell-bearing sidecar built
    /// for the purpose, put back into a stack that has nothing in it to execute on purpose. See
    /// <see cref="RetentionSchedule"/> for why it stays outside the pipeline either way.
    /// </para>
    /// </summary>
    private static Command Retention()
    {
        Option<string?> every = new("--every")
        {
            Description =
                "Keep running and repeat this often: 90m, 24h, 7d, or a hh:mm:ss span, and at "
                + "least a minute. "
                + "Omit for a single pass.",
        };

        Command command = new(
            "retention",
            "Applies the retention schedule: deletes what has aged out, reports what has not.");

        command.Add(every);

        command.SetAction((result, cancellationToken) =>
        {
            string? requested = result.GetValue(every);

            if (requested is null)
            {
                return Runner.RetentionAsync(null, cancellationToken);
            }

            if (!RetentionSchedule.TryParseInterval(requested, out TimeSpan interval, out string? problem))
            {
                Console.Error.WriteLine(problem);
                return Task.FromResult(Runner.MisconfiguredExitCode);
            }

            return Runner.RetentionAsync(interval, cancellationToken);
        });

        return command;
    }

    private static Command Sync()
    {
        Option<Guid> repository =
            new("--repository") { Description = "Source repository identifier.", Required = true };

        Command command = new("sync", "Refreshes the source-system snapshot for one repository.");
        command.Add(Workspace);
        command.Add(Project);
        command.Add(repository);

        command.SetAction((result, cancellationToken) => Runner.InvokeAsync(
            UseCaseCatalog.SyncSources.Name,
            Scoped(
                result.GetRequiredValue(Workspace),
                result.GetRequiredValue(Project),
                ("repositoryId", result.GetRequiredValue(repository))),
            result.GetValue(Actor),
            cancellationToken));

        return command;
    }

    private static Command Reindex()
    {
        Command command = new("reindex", "Rebuilds the search index for one project.");
        command.Add(Workspace);
        command.Add(Project);

        command.SetAction((result, cancellationToken) => Runner.InvokeAsync(
            UseCaseCatalog.Reindex.Name,
            Scoped(result.GetRequiredValue(Workspace), result.GetRequiredValue(Project)),
            result.GetValue(Actor),
            cancellationToken));

        return command;
    }

    /// <summary>A flat JSON object. Null values are dropped rather than sent as nulls.</summary>
    private static string Body(params (string Name, object? Value)[] fields)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach ((string name, object? value) in fields)
        {
            if (value is not null)
            {
                body[name] = value;
            }
        }

        return JsonSerializer.Serialize(body, JsonConventions.Options);
    }

    /// <summary>
    /// The same, with the scope object every project-scoped operation expects. Built here rather
    /// than typed out per command so a command cannot quietly omit the workspace half.
    /// </summary>
    private static string Scoped(
        Guid workspaceId, Guid projectId, params (string Name, object? Value)[] fields)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["scope"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["workspaceId"] = workspaceId,
                ["projectId"] = projectId,
            },
        };

        foreach ((string name, object? value) in fields)
        {
            if (value is not null)
            {
                body[name] = value;
            }
        }

        return JsonSerializer.Serialize(body, JsonConventions.Options);
    }
}
