using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Application.UseCases.Lifecycle;

// Drafting and the controlled lifecycle.
//
// create_draft is the only one of these AI may reach. Approval, correction, publication, and
// archiving stay under human control, and the pipeline refuses the AI channel for them
// independently of what any MCP tool list happens to say.
//
// The rules themselves live in the KnowledgeRecord aggregate. These use cases load, call, and
// save; when the aggregate refuses, the exception becomes a Rejected result rather than being
// swallowed here.

public sealed record CreateDraftRequest(
    ProjectScope Scope,
    WorkItemId WorkItemId,
    RecordKind Kind,
    string Title,
    string Body,
    Provenance Provenance,
    IReadOnlyDictionary<string, string>? FrontMatter = null) : ProjectRequest(Scope)
{
    public override string ResourceReference => WorkItemId.ToString();

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(Title))
        {
            errors.Add("A draft needs a title.");
        }

        if (Body is null)
        {
            errors.Add("A draft needs a body, even an empty one.");
        }

        if (Provenance is null)
        {
            // Provenance is mandatory in the domain too. Catching it here turns a domain
            // exception into a readable validation message for the caller.
            errors.Add("A draft needs provenance: where this came from, who wrote it, and when.");
        }

        return errors;
    }
}

/// <summary>
/// The one write operation AI may perform. What it produces is a draft, and no reader of
/// published knowledge sees it until a human approves it (SB-26).
/// </summary>
public sealed class CreateDraftUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<CreateDraftRequest, LifecycleResult>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CreateDraft;

    protected internal override async Task<LifecycleResult> HandleAsync(
        CreateDraftRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        WorkItem item =
            await _repository.FindWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken)
            ?? throw new ResourceNotFoundException($"No work item {request.WorkItemId} in this project.");

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(),
            request.Scope,
            item.Id,
            request.Kind,
            request.Title,
            request.Body,
            request.FrontMatter,
            request.Provenance,
            _clock.UtcNow,
            caller.UserId);

        await _repository.AddRecordAsync(record, cancellationToken);
        return Describe(record);
    }

    internal static LifecycleResult Describe(KnowledgeRecord record) =>
        new(record.Id, record.Status, record.CurrentRevision.Number, record.PublishedRevisionNumber);
}

public sealed record ReviseDraftRequest(
    ProjectScope Scope,
    KnowledgeRecordId RecordId,
    string Title,
    string Body,
    Provenance Provenance,
    IReadOnlyDictionary<string, string>? FrontMatter = null) : ProjectRequest(Scope)
{
    public override string ResourceReference => RecordId.ToString();

    public override IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(Title) ? ["A revision needs a title."] : [];
}

/// <summary>
/// Adds a revision, usually in response to a correction request. Any earlier approval stays in
/// the history but stops covering the content, so publication is blocked until a human reads the
/// new text.
/// </summary>
public sealed class ReviseDraftUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<ReviseDraftRequest, LifecycleResult>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ReviseDraft;

    protected internal override async Task<LifecycleResult> HandleAsync(
        ReviseDraftRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record = await LifecycleSupport.LoadAsync(
            _repository, request.RecordId, request.Scope, cancellationToken);

        record.AddRevision(
            request.Title, request.Body, request.FrontMatter, request.Provenance, _clock.UtcNow, caller.UserId);

        await _repository.UpdateRecordAsync(record, cancellationToken);
        return CreateDraftUseCase.Describe(record);
    }
}

public sealed record RecordActionRequest(ProjectScope Scope, KnowledgeRecordId RecordId) : ProjectRequest(Scope)
{
    public override string ResourceReference => RecordId.ToString();
}

/// <summary>Moves a draft into review.</summary>
public sealed class SubmitForApprovalUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<RecordActionRequest, LifecycleResult>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.SubmitForApproval;

    protected internal override async Task<LifecycleResult> HandleAsync(
        RecordActionRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record = await LifecycleSupport.LoadAsync(
            _repository, request.RecordId, request.Scope, cancellationToken);

        record.SubmitForApproval(_clock.UtcNow);
        await _repository.UpdateRecordAsync(record, cancellationToken);
        return CreateDraftUseCase.Describe(record);
    }
}

public sealed record ApproveRecordRequest(
    ProjectScope Scope,
    KnowledgeRecordId RecordId,
    string ApprovedContentHash) : ProjectRequest(Scope)
{
    public override string ResourceReference => RecordId.ToString();

    public override IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(ApprovedContentHash)
            ? ["Approval must name the content hash that was reviewed."]
            : [];
}

/// <summary>
/// Records approval of one exact revision.
/// <para>
/// The caller sends the hash it displayed to the reviewer, not a revision number and not an
/// implicit latest. If the record moved on between reading and approving, the aggregate refuses
/// and the caller gets a Rejected result telling them to re-read. That is control SB-23, and it
/// is the reason the approval screen shows a hash at all.
/// </para>
/// <para>
/// Self-approval is permitted: info.md allows a draft creator to approve their own draft under
/// the same permission check as any reviewer. The aggregate records that it happened rather than
/// preventing it.
/// </para>
/// </summary>
public sealed class ApproveRecordUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<ApproveRecordRequest, ApprovalResult>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ApproveRecord;

    protected internal override async Task<ApprovalResult> HandleAsync(
        ApproveRecordRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record = await LifecycleSupport.LoadAsync(
            _repository, request.RecordId, request.Scope, cancellationToken);

        Approval approval = record.Approve(
            caller.UserId, ContentHash.Parse(request.ApprovedContentHash), _clock.UtcNow);

        await _repository.UpdateRecordAsync(record, cancellationToken);

        return new ApprovalResult(
            record.Id,
            record.Status,
            record.CurrentRevision.Number,
            record.PublishedRevisionNumber,
            approval.ApproverId,
            approval.ApprovedContentHash.Value,
            approval.ApprovedRevisionNumber,
            approval.ApprovedAt,
            approval.ApproverWasDraftCreator);
    }
}

public sealed record RequestCorrectionRequest(
    ProjectScope Scope,
    KnowledgeRecordId RecordId,
    string Reason) : ProjectRequest(Scope)
{
    public override string ResourceReference => RecordId.ToString();

    public override IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(Reason)
            ? ["A correction request needs a reason. The reason is part of the knowledge."]
            : [];
}

/// <summary>Sends a record back for changes, keeping the reason.</summary>
public sealed class RequestCorrectionUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<RequestCorrectionRequest, LifecycleResult>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.RequestCorrection;

    protected internal override async Task<LifecycleResult> HandleAsync(
        RequestCorrectionRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record = await LifecycleSupport.LoadAsync(
            _repository, request.RecordId, request.Scope, cancellationToken);

        record.RequestCorrection(caller.UserId, request.Reason, _clock.UtcNow);
        await _repository.UpdateRecordAsync(record, cancellationToken);
        return CreateDraftUseCase.Describe(record);
    }
}

/// <summary>
/// Publishes the approved revision. Rejected unless an approval covers exactly the current
/// content; the aggregate makes that decision, not this class.
/// </summary>
public sealed class PublishRecordUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<RecordActionRequest, LifecycleResult>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.PublishRecord;

    protected internal override async Task<LifecycleResult> HandleAsync(
        RecordActionRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record = await LifecycleSupport.LoadAsync(
            _repository, request.RecordId, request.Scope, cancellationToken);

        record.Publish(_clock.UtcNow);
        await _repository.UpdateRecordAsync(record, cancellationToken);
        return CreateDraftUseCase.Describe(record);
    }
}

/// <summary>Archives a record. Archived records refuse every further change.</summary>
public sealed class ArchiveRecordUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<RecordActionRequest, LifecycleResult>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ArchiveRecord;

    protected internal override async Task<LifecycleResult> HandleAsync(
        RecordActionRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record = await LifecycleSupport.LoadAsync(
            _repository, request.RecordId, request.Scope, cancellationToken);

        record.Archive(_clock.UtcNow);
        await _repository.UpdateRecordAsync(record, cancellationToken);
        return CreateDraftUseCase.Describe(record);
    }
}

internal static class LifecycleSupport
{
    /// <summary>
    /// Loads a record within the caller scope. The scope is passed to the repository rather than
    /// checked afterwards, so a record in another project comes back as not found rather than as
    /// something to filter out later.
    /// </summary>
    public static async Task<KnowledgeRecord> LoadAsync(
        IKnowledgeRepository repository,
        KnowledgeRecordId recordId,
        ProjectScope scope,
        CancellationToken cancellationToken) =>
        await repository.FindRecordAsync(recordId, scope, cancellationToken)
        ?? throw new ResourceNotFoundException($"No record {recordId} in this project.");
}
