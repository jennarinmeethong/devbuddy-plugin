using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Application.UseCases.Handover;

// Work transfer. These three are the reason the system exists: they answer what is left, what
// nobody knows yet, and what is claimed without evidence.

public sealed record WorkItemScopedRequest(ProjectScope Scope, WorkItemId WorkItemId) : ProjectRequest(Scope)
{
    public override string ResourceReference => WorkItemId.ToString();
}

public sealed record HandoverResponse(
    WorkItemId WorkItemId,
    string Title,
    IReadOnlyList<HandoverSection> Sections,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> MissingEvidence,
    DateTimeOffset GeneratedAt) : IRedactableResponse<HandoverResponse>
{
    public HandoverResponse Redact(IRedactor redactor) =>
        this with
        {
            Title = redactor.Redact(Title),
            Sections = [.. Sections.Select(section => section with { Content = redactor.Redact(section.Content) })],
            OpenQuestions = [.. OpenQuestions.Select(redactor.Redact)],
            MissingEvidence = [.. MissingEvidence.Select(redactor.Redact)],
        };
}

/// <summary>One part of a handover, drawn from the published records of one kind.</summary>
public sealed record HandoverSection(RecordKind Kind, string Content, int RecordCount);

/// <summary>
/// Assembles a handover from what has actually been published against a work item.
/// <para>
/// It draws on published records only. A handover built partly from unapproved drafts would hand
/// the next owner statements nobody has reviewed, which is the opposite of what it is for.
/// Anything still in draft is reported as an open question instead.
/// </para>
/// </summary>
public sealed class GenerateHandoverUseCase(IKnowledgeRepository repository, IClock clock)
    : UseCase<WorkItemScopedRequest, HandoverResponse>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.GenerateHandover;

    protected internal override async Task<HandoverResponse> HandleAsync(
        WorkItemScopedRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        WorkItem item =
            await _repository.FindWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken)
            ?? throw new ResourceNotFoundException($"No work item {request.WorkItemId} in this project.");

        IReadOnlyList<KnowledgeRecord> records =
            await _repository.ListRecordsForWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken);

        List<HandoverSection> sections =
        [
            .. records
                .Where(record => record.PublishedRevision is not null)
                .GroupBy(record => record.Kind)
                .OrderBy(group => group.Key)
                .Select(group => new HandoverSection(
                    group.Key,
                    string.Join(
                        Environment.NewLine + Environment.NewLine,
                        group.Select(record => record.PublishedRevision!.Body)),
                    group.Count()))
        ];

        IReadOnlyList<string> openQuestions = CollectOpenQuestions(records, item);
        IReadOnlyList<string> missingEvidence = CollectMissingEvidence(records);

        return new HandoverResponse(
            item.Id, item.Title, sections, openQuestions, missingEvidence, _clock.UtcNow);
    }

    internal static IReadOnlyList<string> CollectOpenQuestions(
        IReadOnlyList<KnowledgeRecord> records, WorkItem item)
    {
        List<string> questions = [];

        if (item.Exclusions is null)
        {
            // The most expensive question a later owner asks is what this work deliberately
            // left out, so its absence is itself an open question.
            questions.Add($"Work item {item.Key} does not record what is out of scope.");
        }

        questions.AddRange(records
            .Where(record => record.Status is not (RecordStatus.Published or RecordStatus.Archived))
            .Select(record =>
                $"Record {record.Id} ({record.Kind}) is {record.Status} and has not been published."));

        questions.AddRange(records
            .SelectMany(record => record.CorrectionRequests.Select(correction => (record, correction)))
            .Where(pair => pair.record.Status == RecordStatus.Draft)
            .Select(pair =>
                $"Correction requested on record {pair.record.Id}: {pair.correction.Reason}"));

        return questions;
    }

    internal static IReadOnlyList<string> CollectMissingEvidence(IReadOnlyList<KnowledgeRecord> records) =>
    [
        .. records
            .Where(record => record.CurrentRevision.Provenance.Evidence.Count == 0)
            .Select(record =>
                $"Record {record.Id} ({record.Kind}) cites no evidence for revision "
                + $"{record.CurrentRevision.Number}.")
    ];
}

public sealed record OpenQuestionsResponse(IReadOnlyList<string> Questions)
    : IRedactableResponse<OpenQuestionsResponse>
{
    public OpenQuestionsResponse Redact(IRedactor redactor) =>
        new([.. Questions.Select(redactor.Redact)]);
}

/// <summary>Everything about this work item that nobody has answered yet.</summary>
public sealed class FindOpenQuestionsUseCase(IKnowledgeRepository repository)
    : UseCase<WorkItemScopedRequest, OpenQuestionsResponse>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.FindOpenQuestions;

    protected internal override async Task<OpenQuestionsResponse> HandleAsync(
        WorkItemScopedRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        WorkItem item =
            await _repository.FindWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken)
            ?? throw new ResourceNotFoundException($"No work item {request.WorkItemId} in this project.");

        IReadOnlyList<KnowledgeRecord> records =
            await _repository.ListRecordsForWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken);

        return new OpenQuestionsResponse(GenerateHandoverUseCase.CollectOpenQuestions(records, item));
    }
}

public sealed record MissingEvidenceResponse(IReadOnlyList<string> Gaps)
    : IRedactableResponse<MissingEvidenceResponse>
{
    public MissingEvidenceResponse Redact(IRedactor redactor) =>
        new([.. Gaps.Select(redactor.Redact)]);
}

/// <summary>
/// Claims with nothing behind them. Also reports evidence that exists but has not been scanned,
/// because unscanned material is not releasable and a handover that depends on it is blocked
/// without saying so (SB-17).
/// </summary>
public sealed class FindMissingEvidenceUseCase(IKnowledgeRepository repository, IEvidenceStore evidenceStore)
    : UseCase<WorkItemScopedRequest, MissingEvidenceResponse>
{
    private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));
    private readonly IEvidenceStore _evidenceStore = Guard.NotNull(evidenceStore, nameof(evidenceStore));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.FindMissingEvidence;

    protected internal override async Task<MissingEvidenceResponse> HandleAsync(
        WorkItemScopedRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<KnowledgeRecord> records =
            await _repository.ListRecordsForWorkItemAsync(request.WorkItemId, request.Scope, cancellationToken);

        List<string> gaps = [.. GenerateHandoverUseCase.CollectMissingEvidence(records)];

        IReadOnlyList<EvidenceObject> stored =
            await _evidenceStore.ListForScopeAsync(request.Scope, cancellationToken);

        gaps.AddRange(stored
            .Where(evidence => !evidence.IsReleasable)
            .Select(evidence =>
                $"Evidence {evidence.Id} is {evidence.RedactionState} and cannot be released."));

        return new MissingEvidenceResponse(gaps);
    }
}
