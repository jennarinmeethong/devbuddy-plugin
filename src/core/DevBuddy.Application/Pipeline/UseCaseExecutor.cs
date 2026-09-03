using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Pipeline;

/// <summary>
/// The single path through which every use case runs.
/// <para>
/// Stages, in order: validate input, resolve identity, check the AI channel against the
/// descriptor, authorise for permission and scope, execute, redact outbound text, audit.
/// </para>
/// <para>
/// Nothing here is optional per use case. That is the point: the security properties the whole
/// design rests on belong to this class, not to thirty separate handlers that each have to
/// remember them.
/// </para>
/// </summary>
public sealed class UseCaseExecutor
{
    private readonly IAuthorizationService _authorization;
    private readonly IAuditSink _auditSink;
    private readonly IRedactor _redactor;
    private readonly ISecretScanner _scanner;
    private readonly IPersonalDataScanner _personalDataScanner;
    private readonly IPersonalDataRedactor _personalDataRedactor;
    private readonly IClock _clock;

    public UseCaseExecutor(
        IAuthorizationService authorization,
        IAuditSink auditSink,
        IRedactor redactor,
        ISecretScanner scanner,
        IPersonalDataScanner personalDataScanner,
        IPersonalDataRedactor personalDataRedactor,
        IClock clock)
    {
        _authorization = Guard.NotNull(authorization, nameof(authorization));
        _auditSink = Guard.NotNull(auditSink, nameof(auditSink));
        _redactor = Guard.NotNull(redactor, nameof(redactor));
        _scanner = Guard.NotNull(scanner, nameof(scanner));
        _personalDataScanner = Guard.NotNull(personalDataScanner, nameof(personalDataScanner));
        _personalDataRedactor = Guard.NotNull(personalDataRedactor, nameof(personalDataRedactor));
        _clock = Guard.NotNull(clock, nameof(clock));
    }

    public async Task<UseCaseResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
        UseCase<TRequest, TResponse> useCase,
        TRequest request,
        CallerContext caller,
        CancellationToken cancellationToken)
        where TRequest : IUseCaseRequest
    {
        Guard.NotNull(useCase, nameof(useCase));
        Guard.NotNull(caller, nameof(caller));

        UseCaseDescriptor descriptor = useCase.Descriptor;

        // 1. Validate. Nothing was authorised and nothing was read, so this is not audited:
        //    a malformed request is a host logging concern, not a data-access event.
        if (request is null)
        {
            return UseCaseResult.Invalid<TResponse>(["The request was missing."]);
        }

        IReadOnlyList<string> errors = request.Validate();
        if (errors.Count > 0)
        {
            return UseCaseResult.Invalid<TResponse>(errors);
        }

        // 2. Resolve identity. Stopping here rather than falling through matters: an anonymous
        //    caller must never reach a permission check that might accidentally pass.
        if (caller.IsAnonymous)
        {
            return await DenyAsync<TResponse>(
                descriptor, request, caller, "No identity was resolved for this request.", cancellationToken);
        }

        // 3. Structural AI check, independent of the MCP tool list, so a mistake there cannot
        //    reach a human-only operation (SB-07).
        if (caller.Channel == AccessChannel.Ai && descriptor.AiExposure != AiExposure.Allowed)
        {
            return await DenyAsync<TResponse>(
                descriptor,
                request,
                caller,
                $"The operation {descriptor.Name} is not available to the AI channel.",
                cancellationToken);
        }

        // 4. Authorise. The scope comes from the request and is verified, never trusted.
        var authorizationRequest = new AuthorizationRequest(
            caller, descriptor.Permission, request.WorkspaceId, request.ProjectId, request.ResourceReference);

        AuthorizationDecision decision =
            await _authorization.AuthorizeAsync(authorizationRequest, cancellationToken);

        if (decision is null || !decision.IsAllowed)
        {
            return await DenyAsync<TResponse>(
                descriptor,
                request,
                caller,
                decision?.Reason ?? "The authorization service returned no decision.",
                cancellationToken);
        }

        // 5. Scan anything that would be written down. Refused, not redacted: storing something
        //    other than what the author wrote, without telling them, is worse than saying no
        //    (SB-17, retention half).
        //
        //    Secrets are checked unconditionally. Personal data is checked only on the AI
        //    channel, and only when the project has no approved bounded scope: a person working
        //    on their own project data is doing ordinary work, which the AI Data Policy has
        //    nothing to say about (SB-18).
        if (request is IScannableRequest scannable)
        {
            List<string> findings = [.. await FindSecretsAsync(scannable, cancellationToken)];

            bool checkPersonalData = caller.Channel == AccessChannel.Ai && decision.BoundedDataScope is null;

            if (checkPersonalData)
            {
                findings.AddRange(await FindPersonalDataAsync(scannable, cancellationToken));
            }

            if (findings.Count > 0)
            {
                await WriteAuditAsync(
                    descriptor,
                    request,
                    caller,
                    AuditAction.ContentScanned,
                    AuditOutcome.Denied,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        // Rule names and positions. Never the matched text.
                        ["blocked_findings"] = string.Join("; ", findings),
                    },
                    cancellationToken);

                return UseCaseResult.Blocked<TResponse>(
                    "The content carries something that must not be stored. Remove it and try again.",
                    findings);
            }
        }

        // 6. Execute.
        TResponse response;
        try
        {
            response = await useCase.HandleAsync(request, caller, cancellationToken);
        }
        catch (ResourceNotFoundException notFound)
        {
            await AuditAsync(descriptor, request, caller, AuditOutcome.Failed, null, cancellationToken);
            return UseCaseResult.NotFound<TResponse>(notFound.Message);
        }
        catch (DomainException rejected)
        {
            // A refused domain rule is recorded with the reason. A rejected publication is
            // exactly the event an investigation into a stale approval would look for.
            await AuditAsync(
                descriptor,
                request,
                caller,
                AuditOutcome.Failed,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["rejection"] = Summarise(rejected) },
                cancellationToken);

            return UseCaseResult.Rejected<TResponse>(rejected.Message);
        }

        // 7. Redact outbound free text before it leaves the boundary (SB-17, egress half).
        //    On the AI channel, with no bounded scope approved for the project, personal data is
        //    redacted from the same response in the same pass (SB-18): a caller who may read the
        //    record at all still does not receive raw customer, production, or personal data
        //    through it without that separate approval.
        if (response is IRedactableResponse<TResponse> redactable)
        {
            IRedactor effective = caller.Channel == AccessChannel.Ai && decision.BoundedDataScope is null
                ? new CompositeRedactor(_redactor, _personalDataRedactor)
                : _redactor;

            response = redactable.Redact(effective);
        }

        // 8. Audit the access that actually happened, with whatever metadata the use case
        //    contributed. Redaction ran first, so nothing a redactor would have removed can
        //    reach the audit store through this path either.
        IReadOnlyDictionary<string, string>? details =
            response is IAuditableResult auditable ? auditable.AuditDetails : null;

        await AuditAsync(descriptor, request, caller, AuditOutcome.Succeeded, details, cancellationToken);

        return UseCaseResult.Success(response);
    }

    private async Task<UseCaseResult<TResponse>> DenyAsync<TResponse>(
        UseCaseDescriptor descriptor,
        IUseCaseRequest request,
        CallerContext caller,
        string reason,
        CancellationToken cancellationToken)
    {
        // A refused cross-project read is exactly the event an investigation needs to find, so
        // denials are audited as deliberately as successes.
        await WriteAuditAsync(
            descriptor, request, caller, AuditAction.AccessDenied, AuditOutcome.Denied, null, cancellationToken);

        return UseCaseResult.Denied<TResponse>(reason);
    }

    /// <summary>
    /// Scans every field the request would have written down, and describes the findings by rule
    /// name and line. The matched text never appears: a finding that quoted the secret it found
    /// would put that secret into the result, the audit entry, and every log that touched either.
    /// </summary>
    private async Task<IReadOnlyList<string>> FindSecretsAsync(
        IScannableRequest scannable, CancellationToken cancellationToken)
    {
        List<string> findings = [];

        foreach (string content in scannable.ContentForScanning)
        {
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            SecretScanResult result = await _scanner.ScanAsync(content, cancellationToken);

            findings.AddRange(result.Findings.Select(
                finding => $"{finding.RuleName} at line {finding.LineNumber}"));
        }

        return findings;
    }

    /// <summary>
    /// The same shape as <see cref="FindSecretsAsync"/>, for personal data, with findings tagged
    /// so an audit reader can tell the two controls apart without the matched text ever appearing.
    /// </summary>
    private async Task<IReadOnlyList<string>> FindPersonalDataAsync(
        IScannableRequest scannable, CancellationToken cancellationToken)
    {
        List<string> findings = [];

        foreach (string content in scannable.ContentForScanning)
        {
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            PersonalDataScanResult result = await _personalDataScanner.ScanAsync(content, cancellationToken);

            findings.AddRange(result.Findings.Select(
                finding => $"personal-data:{finding.RuleName} at line {finding.LineNumber}"));
        }

        return findings;
    }

    /// <summary>
    /// The rule that was broken, not the content that broke it. Domain messages name states,
    /// revision numbers, and short hashes, and the 200-character cap in the domain refuses
    /// anything that grew beyond that.
    /// </summary>
    private static string Summarise(DomainException rejected) =>
        rejected.Message.Length <= 200 ? rejected.Message : rejected.GetType().Name;

    private Task AuditAsync(
        UseCaseDescriptor descriptor,
        IUseCaseRequest request,
        CallerContext caller,
        AuditOutcome outcome,
        IReadOnlyDictionary<string, string>? details,
        CancellationToken cancellationToken) =>
        WriteAuditAsync(descriptor, request, caller, descriptor.AuditAction, outcome, details, cancellationToken);

    private Task WriteAuditAsync(
        UseCaseDescriptor descriptor,
        IUseCaseRequest request,
        CallerContext caller,
        AuditAction action,
        AuditOutcome outcome,
        IReadOnlyDictionary<string, string>? details,
        CancellationToken cancellationToken)
    {
        // The reference identifies what was acted on. The content itself never appears here.
        string reference = $"{descriptor.Name}:{request.ResourceReference}";

        AuditEvent entry = request.ProjectId is { } projectId
            ? AuditEvent.ForProject(
                AuditEventId.New(),
                new ProjectScope(request.WorkspaceId, projectId),
                caller.UserId,
                action,
                outcome,
                reference,
                _clock.UtcNow,
                details)
            : AuditEvent.ForWorkspace(
                AuditEventId.New(),
                request.WorkspaceId,
                caller.UserId,
                action,
                outcome,
                reference,
                _clock.UtcNow,
                details);

        return _auditSink.WriteAsync(entry, cancellationToken);
    }
}

/// <summary>
/// Applies a secret redactor and a personal-data redactor in one pass, so a response type's
/// <c>Redact(IRedactor)</c> method never has to know that a second control exists.
/// <para>
/// Secrets go first. A value that is a secret and happens to also look like a labelled personal
/// field is redacted once either way, but running secrets first means the marker it leaves behind
/// cannot be mistaken for personal data by the second pass.
/// </para>
/// </summary>
internal sealed class CompositeRedactor(IRedactor secrets, IPersonalDataRedactor personalData) : IRedactor
{
    public string Redact(string text) => personalData.Redact(secrets.Redact(text));
}
