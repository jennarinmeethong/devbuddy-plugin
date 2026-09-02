using System.Security.Claims;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Persistence;

namespace DevBuddy.Api;

/// <summary>
/// The operation surface, over HTTP.
/// <para>
/// One route for every operation rather than forty hand-written ones. That is not laziness: the
/// pipeline already decides what an operation is, what it needs, and what it returns, and giving
/// each one a bespoke endpoint would be forty places for a transport to disagree with it. The
/// OpenAPI document still describes them, because the dispatcher can enumerate them.
/// </para>
/// <para>
/// Everything here runs on the Human channel. The channel is decided by which host is running,
/// never by anything in the request: an AI caller reaches the MCP server, and that server sets the
/// AI channel for every call it makes (SB-08).
/// </para>
/// </summary>
internal static class OperationEndpoints
{
    public static void MapOperations(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/operations", (OperationDispatcher dispatcher) => Results.Ok(Manifest(dispatcher)))
            .RequireAuthorization()
            .WithName("ListOperations")
            .WithSummary(
                "Every operation this deployment can perform, what each one needs, and the JSON "
                + "shape of its arguments and its result. The web client is generated from this.");

        routes.MapPost("/operations/{name}", InvokeAsync)
            .RequireAuthorization()
            .WithName("InvokeOperation")
            .WithSummary("Runs one operation. The body is the operation arguments.");

        routes.MapGet(
            "/workspaces/{workspaceId:guid}/projects/{projectId:guid}/evidence/{evidenceId:guid}",
            DownloadEvidenceAsync)
            .RequireAuthorization()
            .WithName("DownloadEvidence")
            .WithSummary("Streams one stored artefact after an authorization check.");
    }

    /// <summary>
    /// The operation manifest: what exists, what it needs, and what it looks like on the wire.
    /// <para>
    /// The schemas are generated from the request and response records, which is what makes this
    /// usable as a contract rather than as documentation. The web client is generated from it, so
    /// a record that changes shape changes the client, and a client that was not regenerated
    /// fails its drift test rather than failing in a browser.
    /// </para>
    /// </summary>
    private static List<OperationManifestEntry> Manifest(OperationDispatcher dispatcher)
    {
        List<OperationManifestEntry> entries = [];

        foreach (UseCaseDescriptor descriptor in dispatcher.Operations)
        {
            OperationBinding binding = dispatcher.Find(descriptor.Name)
                ?? throw new InvalidOperationException($"No binding for {descriptor.Name}.");

            entries.Add(new OperationManifestEntry(
                descriptor.Name,
                descriptor.Permission.ToString(),
                descriptor.AiExposure == AiExposure.Allowed,
                OperationSchemas.For(binding.RequestType),
                OperationSchemas.For(binding.ResponseType)));
        }

        return entries;
    }

    /// <summary>
    /// Runs an operation. The workspace is taken from the arguments so the tenant context can be
    /// entered before anything queries; it is still only a claim, and the pipeline verifies it.
    /// </summary>
    private static async Task<IResult> InvokeAsync(
        string name,
        JsonElement arguments,
        HttpContext context,
        OperationDispatcher dispatcher,
        MutableTenantContext tenant,
        IWorkspaceResolver workspaces,
        CancellationToken cancellationToken)
    {
        if (string.Equals(name, UseCaseCatalog.DownloadEvidence.Name, StringComparison.Ordinal))
        {
            // Streaming bytes is not a JSON operation. It has a route of its own, and pointing at
            // it is more useful than a serialisation failure.
            return Results.Problem(
                title: "Use the evidence route",
                detail: "download_evidence returns a stream. Use the evidence endpoint instead.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await WorkspaceEntry.EnterAsync(arguments, tenant, workspaces, cancellationToken);

        DispatchResult result = await dispatcher.InvokeAsync(
            name, arguments, CallerFor(context), cancellationToken);

        return Render(result);
    }

    private static async Task<IResult> DownloadEvidenceAsync(
        Guid workspaceId,
        Guid projectId,
        Guid evidenceId,
        HttpContext context,
        DownloadEvidenceUseCase useCase,
        UseCaseExecutor executor,
        MutableTenantContext tenant,
        CancellationToken cancellationToken)
    {
        var scope = new ProjectScope(new WorkspaceId(workspaceId), new ProjectId(projectId));
        tenant.EnterWorkspace(scope.WorkspaceId);

        // Through the pipeline like everything else, which is why there is no presigned URL: the
        // scope check, the released-state check, and the audit entry all happen here (SB-12).
        UseCaseResult<EvidenceDownloadResponse> result = await executor.ExecuteAsync(
            useCase,
            new DownloadEvidenceRequest(scope, new EvidenceObjectId(evidenceId)),
            CallerFor(context),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Render(new DispatchResult(
                UseCaseCatalog.DownloadEvidence.Name, result.Outcome, null, result.Reason, result.ValidationErrors));
        }

        EvidenceDownloadResponse evidence = result.Value!;
        return Results.Stream(evidence.Content, evidence.MediaType);
    }

    /// <summary>
    /// The caller, built from the authenticated principal. Never from the request body: a body
    /// that could name its own actor would make every other check decorative.
    /// </summary>
    private static CallerContext CallerFor(HttpContext context)
    {
        string? subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        UserId actor = Guid.TryParse(subject, out Guid parsed) ? new UserId(parsed) : default;

        return new CallerContext(actor, AccessChannel.Human, context.TraceIdentifier);
    }

    /// <summary>
    /// Maps an outcome onto a status code.
    /// <para>
    /// Denied is 403 and NotFound is 404, and the pipeline never converts one into the other.
    /// Whether to hide a denial behind a 404 is a decision about a specific resource, and making it
    /// silently here would apply it everywhere without anyone choosing to.
    /// </para>
    /// </summary>
    private static IResult Render(DispatchResult result) => result.Outcome switch
    {
        ExecutionOutcome.Succeeded => Results.Ok(result.Payload),
        ExecutionOutcome.Invalid => Problem(result, StatusCodes.Status400BadRequest, "Invalid request"),
        ExecutionOutcome.Blocked => Problem(result, StatusCodes.Status422UnprocessableEntity, "Blocked content"),
        ExecutionOutcome.Denied => Problem(result, StatusCodes.Status403Forbidden, "Refused"),
        ExecutionOutcome.NotFound => Problem(result, StatusCodes.Status404NotFound, "Not found"),
        ExecutionOutcome.Rejected => Problem(result, StatusCodes.Status409Conflict, "Refused by a rule"),
        _ => Problem(result, StatusCodes.Status500InternalServerError, "Failed"),
    };

    private static IResult Problem(DispatchResult result, int status, string title) =>
        Results.Problem(
            title: title,
            detail: result.Reason,
            statusCode: status,
            extensions: result.Details.Count == 0
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal) { ["details"] = result.Details });
}

/// <summary>One operation, as the manifest describes it.</summary>
internal sealed record OperationManifestEntry(
    string Name,
    string Permission,
    bool AvailableToAi,
    JsonElement ArgumentsSchema,
    JsonElement ResultSchema);
