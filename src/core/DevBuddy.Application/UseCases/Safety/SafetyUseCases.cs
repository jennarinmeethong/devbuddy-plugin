using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Safety;

// Secret detection and redaction.
//
// Both are denied to the AI channel on purpose. The scanner is a control applied to what AI
// receives, not a service offered to it: a tool that reports where secrets are is a tool that
// helps find them. Inside the system these run in the pipeline, before material is retained and
// before any response leaves the boundary (SB-17).

public sealed record ScanContentRequest(ProjectScope Scope, string Content) : ProjectRequest(Scope)
{
    public override string ResourceReference => "content";

    public override IReadOnlyList<string> Validate() =>
        Content is null ? ["Content is required."] : [];
}

public sealed record SecretScanResponse(bool HasFindings, IReadOnlyList<SecretFinding> Findings);

/// <summary>
/// Scans supplied material for credentials before it is retained.
/// <para>
/// The response carries rule names and positions, never the matched values. A finding that
/// quoted the secret it found would put that secret into every log and bug report that touched
/// the result.
/// </para>
/// </summary>
public sealed class DetectSecretsUseCase(ISecretScanner scanner)
    : UseCase<ScanContentRequest, SecretScanResponse>
{
    private readonly ISecretScanner _scanner = Guard.NotNull(scanner, nameof(scanner));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.DetectSecrets;

    protected internal override async Task<SecretScanResponse> HandleAsync(
        ScanContentRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        SecretScanResult result = await _scanner.ScanAsync(request.Content, cancellationToken);
        return new SecretScanResponse(result.HasFindings, result.Findings);
    }
}

public sealed record RedactionResponse(string RedactedContent, int FindingCount);

/// <summary>
/// Returns supplied material with anything the rules match replaced.
/// <para>
/// Accepted limitation AL-2 applies: detection can miss material. This use case is a control, not
/// a guarantee, and the system depends on provenance and correction for what it misses.
/// </para>
/// </summary>
public sealed class RedactSensitiveDataUseCase(ISecretScanner scanner, IRedactor redactor)
    : UseCase<ScanContentRequest, RedactionResponse>
{
    private readonly ISecretScanner _scanner = Guard.NotNull(scanner, nameof(scanner));
    private readonly IRedactor _redactor = Guard.NotNull(redactor, nameof(redactor));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.RedactSensitiveData;

    protected internal override async Task<RedactionResponse> HandleAsync(
        ScanContentRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        SecretScanResult result = await _scanner.ScanAsync(request.Content, cancellationToken);
        return new RedactionResponse(_redactor.Redact(request.Content), result.Findings.Count);
    }
}
