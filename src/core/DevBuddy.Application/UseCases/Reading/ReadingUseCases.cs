using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Application.UseCases.Reading;

// The read side. Every one of these is exposed to AI, and every one of them returns a view whose
// free-text fields go through the redactor before leaving the pipeline.

public sealed record SearchKnowledgeRequest(
    ProjectScope Scope,
    string QueryText,
    IReadOnlyList<RecordKind>? Kinds = null,
    IReadOnlyList<RecordStatus>? Statuses = null,
    int MaxResults = 20) : ProjectRequest(Scope)
{
    public override string ResourceReference => "search";

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(QueryText))
        {
            errors.Add("A search needs query text.");
        }

        if (MaxResults is < 1 or > 200)
        {
            // An unbounded result set is an availability problem as much as a usability one.
            errors.Add("MaxResults must be between 1 and 200.");
        }

        return errors;
    }
}

public sealed record SearchKnowledgeResponse(IReadOnlyList<KnowledgeSearchHit> Hits)
    : IRedactableResponse<SearchKnowledgeResponse>
{
    public SearchKnowledgeResponse Redact(IRedactor redactor) =>
        new([.. Hits.Select(hit => hit with
        {
            Title = redactor.Redact(hit.Title),
            Snippet = redactor.Redact(hit.Snippet),
        })]);
}

/// <summary>
/// Full-text search with structured filters. Scope is part of the criteria rather than an
/// optional filter, so a query that forgot to scope itself does not compile (SB-12).
/// </summary>
public sealed class SearchKnowledgeUseCase(ISearchIndex searchIndex)
    : UseCase<SearchKnowledgeRequest, SearchKnowledgeResponse>
{
    private readonly ISearchIndex _searchIndex = Guard.NotNull(searchIndex, nameof(searchIndex));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.SearchKnowledge;

    protected internal override async Task<SearchKnowledgeResponse> HandleAsync(
        SearchKnowledgeRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        var criteria = new KnowledgeSearchCriteria(
            request.Scope, request.QueryText, request.Kinds, request.Statuses, request.MaxResults);

        IReadOnlyList<KnowledgeSearchHit> hits = await _searchIndex.SearchAsync(criteria, cancellationToken);
        return new SearchKnowledgeResponse(hits);
    }
}

public sealed record GetRecordRequest(ProjectScope Scope, KnowledgeRecordId RecordId, int? RevisionNumber = null)
    : ProjectRequest(Scope)
{
    public override string ResourceReference => RecordId.ToString();
}

/// <summary>
/// Reads one record. Defaults to the published revision when there is one, so a caller who does
/// not ask for a specific revision never accidentally reads unapproved text (SB-26).
/// </summary>
public sealed class GetRecordUseCase(IKnowledgeRepository repository)
    : UseCase<GetRecordRequest, KnowledgeRecordView>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.GetRecord;

    protected internal override async Task<KnowledgeRecordView> HandleAsync(
        GetRecordRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record =
            await _repository.FindRecordAsync(request.RecordId, request.Scope, cancellationToken)
            ?? throw new ResourceNotFoundException($"No record {request.RecordId} in this project.");

        RecordRevision revision = SelectRevision(record, request.RevisionNumber);
        return KnowledgeRecordView.From(record, revision);
    }

    private static RecordRevision SelectRevision(KnowledgeRecord record, int? requested)
    {
        if (requested is { } number)
        {
            return record.Revisions.FirstOrDefault(revision => revision.Number == number)
                ?? throw new ResourceNotFoundException($"Revision {number} does not exist on this record.");
        }

        return record.PublishedRevision ?? record.CurrentRevision;
    }
}

public sealed record GetWorkItemRequest(ProjectScope Scope, WorkItemId WorkItemId) : ProjectRequest(Scope)
{
    public override string ResourceReference => WorkItemId.ToString();
}

/// <summary>Reads work identity and how many knowledge records hang off it.</summary>
public sealed class GetWorkItemUseCase(IKnowledgeRepository repository)
    : UseCase<GetWorkItemRequest, WorkItemView>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.GetWorkItem;

    protected internal override async Task<WorkItemView> HandleAsync(
        GetWorkItemRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        WorkItem item =
            await _repository.FindWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken)
            ?? throw new ResourceNotFoundException($"No work item {request.WorkItemId} in this project.");

        IReadOnlyList<KnowledgeRecord> records =
            await _repository.ListRecordsForWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken);

        return WorkItemView.From(item, records.Count);
    }
}

public sealed record ListProjectsRequest(WorkspaceId WorkspaceId) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => "projects";
}

public sealed record ListProjectsResponse(IReadOnlyList<ProjectSummary> Projects);

/// <summary>
/// Lists the projects the caller is a member of. The user identifier passed to the directory is
/// the caller, never the AI credential, which is what narrows AI results to the requesting user
/// (SB-09).
/// </summary>
public sealed class ListProjectsUseCase(IProjectDirectory directory)
    : UseCase<ListProjectsRequest, ListProjectsResponse>
{
    private readonly IProjectDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListProjects;

    protected internal override async Task<ListProjectsResponse> HandleAsync(
        ListProjectsRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<Project> projects = await _directory.ListProjectsForUserAsync(
            request.WorkspaceId, caller.UserId, cancellationToken);

        return new ListProjectsResponse(
            [.. projects.Select(project => new ProjectSummary(project.Id, project.Name, project.CreatedAt))]);
    }
}

public sealed record ViewRecordHistoryRequest(ProjectScope Scope, KnowledgeRecordId RecordId)
    : ProjectRequest(Scope)
{
    public override string ResourceReference => RecordId.ToString();
}

public sealed record RecordHistoryResponse(
    KnowledgeRecordId RecordId,
    RecordStatus Status,
    IReadOnlyList<RevisionSummary> Revisions) : IRedactableResponse<RecordHistoryResponse>
{
    public RecordHistoryResponse Redact(IRedactor redactor) =>
        this with { Revisions = [.. Revisions.Select(revision => revision.Redact(redactor))] };
}

/// <summary>
/// The full revision and approval history. Each revision carries its content hash and the
/// approval that covers it, so a reader can see exactly what was approved and what was not.
/// </summary>
public sealed class ViewRecordHistoryUseCase(IKnowledgeRepository repository)
    : UseCase<ViewRecordHistoryRequest, RecordHistoryResponse>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ViewRecordHistory;

    protected internal override async Task<RecordHistoryResponse> HandleAsync(
        ViewRecordHistoryRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        KnowledgeRecord record =
            await _repository.FindRecordAsync(request.RecordId, request.Scope, cancellationToken)
            ?? throw new ResourceNotFoundException($"No record {request.RecordId} in this project.");

        List<RevisionSummary> revisions = [];

        foreach (RecordRevision revision in record.Revisions)
        {
            Approval? approval = record.Approvals
                .LastOrDefault(candidate => candidate.ApprovedContentHash == revision.ContentHash);

            revisions.Add(new RevisionSummary(
                revision.Number,
                revision.ContentHash.Value,
                revision.Title,
                revision.CreatedAt,
                ProvenanceView.From(revision.Provenance),
                IsPublished: record.PublishedRevisionNumber == revision.Number,
                approval is null
                    ? null
                    : new ApprovalSummary(
                        approval.ApproverId,
                        approval.ApprovedContentHash.Value,
                        approval.ApprovedRevisionNumber,
                        approval.ApprovedAt,
                        approval.ApproverWasDraftCreator)));
        }

        return new RecordHistoryResponse(record.Id, record.Status, revisions);
    }
}

public sealed record CompareSnapshotsRequest(
    ProjectScope Scope,
    SourceRepositoryId RepositoryId,
    string EarlierReference,
    string LaterReference) : ProjectRequest(Scope)
{
    public override string ResourceReference => RepositoryId.ToString();

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(EarlierReference) || string.IsNullOrWhiteSpace(LaterReference))
        {
            errors.Add("Both snapshot references are required.");
        }

        return errors;
    }
}

public sealed record SnapshotComparisonResponse(IReadOnlyList<SnapshotDifference> Differences)
    : IRedactableResponse<SnapshotComparisonResponse>
{
    public SnapshotComparisonResponse Redact(IRedactor redactor) =>
        new([.. Differences.Select(difference => difference with
        {
            Before = redactor.Redact(difference.Before),
            After = redactor.Redact(difference.After),
        })]);
}

/// <summary>
/// Compares two source snapshots so a record can be checked against its origin. Divergence is
/// reported for a human to interpret, never resolved automatically (ADR-0010).
/// </summary>
public sealed class CompareSnapshotsUseCase(ISourceSystemClient sourceSystem)
    : UseCase<CompareSnapshotsRequest, SnapshotComparisonResponse>
{
    private readonly ISourceSystemClient _sourceSystem = Guard.NotNull(sourceSystem, nameof(sourceSystem));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CompareSnapshots;

    protected internal override async Task<SnapshotComparisonResponse> HandleAsync(
        CompareSnapshotsRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        SourceSnapshot current =
            await _sourceSystem.FetchSnapshotAsync(request.RepositoryId, request.Scope, cancellationToken);

        SourceSnapshot earlier = current with { Reference = request.EarlierReference };
        SourceSnapshot later = current with { Reference = request.LaterReference };

        IReadOnlyList<SnapshotDifference> differences =
            await _sourceSystem.CompareAsync(earlier, later, cancellationToken);

        return new SnapshotComparisonResponse(differences);
    }
}
