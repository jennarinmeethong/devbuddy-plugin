using System.Text;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Evidence;

/// <summary>
/// Putting an artefact into a project, and seeing what is in one.
/// <para>
/// The evidence store had a read side and no write side: <c>download_evidence</c> existed, backup
/// and restore carried the bytes, retention swept them, and the isolation tests covered them — but
/// nothing a person could reach ever called <c>StoreAsync</c>, so no installation could have had
/// anything to download. These are the missing half.
/// </para>
/// </summary>
public sealed record CaptureEvidenceRequest(
    ProjectScope Scope,
    string MediaType,
    string Description,
    byte[] Content) : ProjectRequest(Scope), IScannableRequest
{
    public override string ResourceReference => Description;

    /// <summary>
    /// The bytes, as text, so the pipeline's own scanner sees them before anything is written.
    /// <para>
    /// This is why capture takes an array rather than a stream: the scan has to happen before the
    /// store is touched, so a file carrying a credential is refused with nothing retained — the
    /// shape SB-17 already has for drafts. A stream could only be scanned by consuming it, and a
    /// consumed stream cannot then be stored.
    /// </para>
    /// <para>
    /// Binary content decodes to nonsense and the scanner finds nothing in it. That is the honest
    /// outcome rather than a pretended one: a credential inside a compressed archive is not
    /// visible to a text scanner, which is what accepted limitation AL-2 already says.
    /// </para>
    /// </summary>
    public IEnumerable<string> ContentForScanning
    {
        get
        {
            yield return Description;
            yield return Encoding.UTF8.GetString(Content);
        }
    }

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (Content is not { Length: > 0 })
        {
            errors.Add("Evidence content cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(MediaType))
        {
            errors.Add("Evidence needs a media type.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            // Required rather than optional, and this is why: an artefact nobody described is one
            // a later owner has to open to understand, which is the thing EvidenceReference's own
            // summary says a description exists to prevent.
            errors.Add("Evidence needs a description saying what it shows.");
        }

        return errors;
    }
}

public sealed record CaptureEvidenceResponse(
    EvidenceObjectId EvidenceId, string ContentHash, long SizeBytes, RedactionState RedactionState);

/// <summary>Stores an artefact the pipeline has already scanned, and records that it is clean.</summary>
public sealed class CaptureEvidenceUseCase(IEvidenceStore evidenceStore)
    : UseCase<CaptureEvidenceRequest, CaptureEvidenceResponse>
{
    private readonly IEvidenceStore _evidenceStore = Guard.NotNull(evidenceStore, nameof(evidenceStore));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CaptureEvidence;

    protected internal override async Task<CaptureEvidenceResponse> HandleAsync(
        CaptureEvidenceRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(request.Content, writable: false);

        EvidenceObject stored = await _evidenceStore.StoreAsync(
            request.Scope, content, request.MediaType, caller.UserId, cancellationToken);

        // Clean, because the pipeline scanned this on the way in and would have Blocked the call
        // before it reached here otherwise. Stored evidence begins NotScanned and the download
        // refuses to release anything in that state, so skipping this would write bytes that
        // nobody could ever read back.
        EvidenceObject scanned = await _evidenceStore.RecordScanResultAsync(
            stored, RedactionState.Clean, cancellationToken);

        return new CaptureEvidenceResponse(
            scanned.Id, scanned.ContentHash.Value, scanned.SizeBytes, scanned.RedactionState);
    }
}

public sealed record ListEvidenceRequest(ProjectScope Scope) : ProjectRequest(Scope)
{
    public override string ResourceReference => Scope.ProjectId.ToString();
}

public sealed record EvidenceSummary(
    EvidenceObjectId EvidenceId,
    string MediaType,
    long SizeBytes,
    DateTimeOffset CapturedAt,
    UserId CapturedBy,
    RedactionState RedactionState,
    bool IsReleasable);

public sealed record ListEvidenceResponse(IReadOnlyList<EvidenceSummary> Evidence);

/// <summary>
/// What a project holds. Metadata only — the bytes need <c>download_evidence</c>, a separate
/// operation with its own audit entry, because listing what exists and reading what is inside it
/// are different questions to ask about a project (SB-12).
/// </summary>
public sealed class ListEvidenceUseCase(IEvidenceStore evidenceStore)
    : UseCase<ListEvidenceRequest, ListEvidenceResponse>
{
    private readonly IEvidenceStore _evidenceStore = Guard.NotNull(evidenceStore, nameof(evidenceStore));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListEvidence;

    protected internal override async Task<ListEvidenceResponse> HandleAsync(
        ListEvidenceRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<EvidenceObject> evidence =
            await _evidenceStore.ListForScopeAsync(request.Scope, cancellationToken);

        return new ListEvidenceResponse(
        [
            .. evidence
                .OrderByDescending(item => item.CapturedAt)
                .Select(item => new EvidenceSummary(
                    item.Id,
                    item.MediaType,
                    item.SizeBytes,
                    item.CapturedAt,
                    item.CapturedBy,
                    item.RedactionState,
                    item.IsReleasable)),
        ]);
    }
}
