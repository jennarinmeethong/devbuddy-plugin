using System.Text.Json;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Persistence;
using ModelContextProtocol.Protocol;

namespace DevBuddy.McpServer;

/// <summary>
/// Answers the two MCP requests that matter here: what tools are there, and run this one.
/// <para>
/// Both are thin on purpose. The tool list comes from <see cref="ToolSurface"/>, the work goes to
/// the dispatcher, and the pipeline behind it decides everything about identity, permission, AI
/// access, redaction, and audit. This class contributes no policy of its own, which is what makes
/// the MCP surface a view over the system rather than a second implementation of it.
/// </para>
/// </summary>
public sealed class McpToolHandlers(
    OperationDispatcher dispatcher, CallerContext caller, TenantEntry? tenant = null)
{
    private readonly OperationDispatcher _dispatcher =
        dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    private readonly CallerContext _caller = caller ?? throw new ArgumentNullException(nameof(caller));

    /// <summary>
    /// How this call enters a tenant. Optional because the tool list is a question about the
    /// catalogue and needs no database; when it is absent, no workspace is entered and the query
    /// filters see nothing, which is the safe direction for a handler that was composed wrongly.
    /// </summary>
    private readonly TenantEntry? _tenant = tenant;

    public ListToolsResult ListTools() => new() { Tools = [.. ToolSurface.Describe(_dispatcher)] };

    public async Task<CallToolResult> CallToolAsync(
        CallToolRequestParams request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        JsonElement arguments = request.Arguments is null
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : JsonSerializer.SerializeToElement(request.Arguments, JsonConventions.Options);

        if (_tenant is not null)
        {
            await WorkspaceEntry.EnterAsync(
                arguments, _tenant.Tenant, _tenant.Workspaces, cancellationToken);
        }

        DispatchResult result = await _dispatcher.InvokeAsync(
            request.Name, arguments, _caller, cancellationToken);

        return result.IsSuccess ? Success(result) : Failure(result);
    }

    private static CallToolResult Success(DispatchResult result) => new()
    {
        IsError = false,
        Content = [Text(result.Payload?.GetRawText() ?? "{}")],
        StructuredContent = result.Payload,
    };

    /// <summary>
    /// A refusal, rendered as a tool error with the reason the pipeline gave.
    /// <para>
    /// The reason is the one written for a caller: it names the rule, never the resource it was
    /// protecting. "The caller has no access to this scope" tells a model to stop; "record 42 in
    /// project Beta exists but is not yours" would have told it something it had no right to know.
    /// </para>
    /// </summary>
    private static CallToolResult Failure(DispatchResult result)
    {
        string detail = result.Details.Count == 0
            ? result.Reason
            : $"{result.Reason} {string.Join(" ", result.Details)}";

        return new CallToolResult
        {
            IsError = true,
            Content = [Text($"{Describe(result.Outcome)}: {detail}")],
        };
    }

    private static string Describe(ExecutionOutcome outcome) => outcome switch
    {
        ExecutionOutcome.Invalid => "The arguments were not valid",
        ExecutionOutcome.Denied => "Refused",
        ExecutionOutcome.NotFound => "Not found",
        ExecutionOutcome.Rejected => "Refused by a rule",
        ExecutionOutcome.Blocked => "Blocked: the content carries something that must not be stored",
        _ => "Failed",
    };

    private static TextContentBlock Text(string text) => new() { Text = text };
}

/// <summary>
/// The two pieces needed to enter a workspace for one call, taken from the request scope.
/// <para>
/// A record rather than two constructor parameters so the pair travels together. Entering a
/// workspace without the resolver would break single-workspace mode; having the resolver without
/// somewhere to put the answer would do nothing at all.
/// </para>
/// </summary>
public sealed record TenantEntry(MutableTenantContext Tenant, IWorkspaceResolver Workspaces);
