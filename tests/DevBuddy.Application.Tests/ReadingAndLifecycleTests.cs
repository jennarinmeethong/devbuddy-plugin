using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// What the read and lifecycle use cases actually do, once the pipeline has let them run.
/// </summary>
public sealed class ReadingAndLifecycleTests
{
    [Fact]
    public async Task get_record_returns_the_published_revision_rather_than_the_newest_draft()
    {
        var harness = new Harness();
        KnowledgeRecord record = Published(harness, publishedBody: "Approved text.");

        record.AddRevision(
            "Title", "Unapproved draft text.", null, TestData.Provenance, TestData.Now, TestData.Author);

        harness.Ports.Record = record;

        KnowledgeRecordView view = await harness.SucceedAsync(
            new GetRecordUseCase(harness.Ports), new GetRecordRequest(TestData.Scope, record.Id));

        // Control SB-26. A caller who did not ask for a specific revision must never be handed
        // text nobody has reviewed.
        Assert.Equal("Approved text.", view.Body);
        Assert.Equal(1, view.RevisionNumber);
        Assert.Equal(1, view.PublishedRevisionNumber);
    }

    [Fact]
    public async Task get_record_can_return_a_named_revision_and_refuses_one_that_does_not_exist()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft("First.");

        KnowledgeRecordView view = await harness.SucceedAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id, RevisionNumber: 1));

        Assert.Equal("First.", view.Body);

        UseCaseResult<KnowledgeRecordView> missing = await harness.RunAsync(
            new GetRecordUseCase(harness.Ports),
            new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id, RevisionNumber: 9));

        Assert.Equal(ExecutionOutcome.NotFound, missing.Outcome);
    }

    [Fact]
    public async Task record_history_shows_which_revision_each_approval_covers()
    {
        var harness = new Harness();
        KnowledgeRecord record = Published(harness);
        record.AddRevision("Title", "Later text.", null, TestData.Provenance, TestData.Now, TestData.Author);
        harness.Ports.Record = record;

        RecordHistoryResponse history = await harness.SucceedAsync(
            new ViewRecordHistoryUseCase(harness.Ports),
            new ViewRecordHistoryRequest(TestData.Scope, record.Id));

        Assert.Equal(2, history.Revisions.Count);

        RevisionSummary first = history.Revisions[0];
        Assert.NotNull(first.Approval);
        Assert.Equal(first.ContentHash, first.Approval.ApprovedContentHash);
        Assert.True(first.IsPublished);

        // The second revision has no approval, and the history says so rather than implying the
        // earlier approval carries over.
        Assert.Null(history.Revisions[1].Approval);
        Assert.False(history.Revisions[1].IsPublished);
    }

    [Fact]
    public async Task list_projects_asks_the_directory_for_the_calling_user()
    {
        var harness = new Harness();

        ListProjectsResponse response = await harness.SucceedAsync(
            new ListProjectsUseCase(harness.Ports), new ListProjectsRequest(TestData.Workspace));

        // Narrowed to the requesting user, not to whatever credential the AI host holds (SB-09).
        Assert.Single(response.Projects);
        Assert.Equal(TestData.ProjectAlpha, response.Projects[0].ProjectId);
    }

    [Fact]
    public async Task get_work_item_reports_the_exclusions_a_later_owner_needs()
    {
        var harness = new Harness();
        harness.Ports.WorkItem!.SetScope("The importer.", "The reporting pipeline.");
        harness.Ports.RecordsForWorkItem.Add(TestData.NewDraft());

        WorkItemView view = await harness.SucceedAsync(
            new GetWorkItemUseCase(harness.Ports),
            new GetWorkItemRequest(TestData.Scope, TestData.WorkItem));

        Assert.Equal("The reporting pipeline.", view.Exclusions);
        Assert.Equal(1, view.RecordCount);
    }

    [Fact]
    public async Task create_draft_stores_a_draft_and_records_who_wrote_it()
    {
        var harness = new Harness();

        LifecycleResult result = await harness.SucceedAsync(
            new CreateDraftUseCase(harness.Ports, harness.Ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Handover,
                "What is left", "Two migrations remain.", TestData.Provenance));

        Assert.Equal(RecordStatus.Draft, result.Status);
        Assert.Null(result.PublishedRevisionNumber);

        KnowledgeRecord saved = Assert.IsType<KnowledgeRecord>(harness.Ports.Saved);
        Assert.Equal(TestData.Author, saved.CreatedBy);
        Assert.Equal(RecordKind.Handover, saved.Kind);
    }

    [Fact]
    public async Task create_draft_refuses_a_draft_with_no_provenance()
    {
        var harness = new Harness();

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            new CreateDraftUseCase(harness.Ports, harness.Ports),
            new CreateDraftRequest(
                TestData.Scope, TestData.WorkItem, RecordKind.Decision, "Title", "Body", Provenance: null!));

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
        Assert.Contains("provenance", string.Join(" ", result.ValidationErrors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task approving_a_hash_that_is_no_longer_current_is_rejected()
    {
        var harness = new Harness();
        KnowledgeRecord record = TestData.NewDraft("First text.");
        ContentHash staleHash = record.CurrentRevision.ContentHash;

        record.AddRevision("Title", "Second text.", null, TestData.Provenance, TestData.Now, TestData.Author);
        record.SubmitForApproval(TestData.Now);
        harness.Ports.Record = record;

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            new ApproveRecordUseCase(harness.Ports, harness.Ports),
            new ApproveRecordRequest(TestData.Scope, record.Id, staleHash.Value));

        // Control SB-23 reaching the caller as an ordinary result: re-read and approve again.
        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);
        Assert.Contains("current revision", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(record.Approvals);
    }

    [Fact]
    public async Task the_full_lifecycle_runs_from_draft_to_published()
    {
        var harness = new Harness();
        KnowledgeRecord record = TestData.NewDraft();
        harness.Ports.Record = record;

        await harness.SucceedAsync(
            new SubmitForApprovalUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, record.Id));

        await harness.SucceedAsync(
            new ApproveRecordUseCase(harness.Ports, harness.Ports),
            new ApproveRecordRequest(TestData.Scope, record.Id, record.CurrentRevision.ContentHash.Value));

        LifecycleResult published = await harness.SucceedAsync(
            new PublishRecordUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, record.Id));

        Assert.Equal(RecordStatus.Published, published.Status);
        Assert.Equal(1, published.PublishedRevisionNumber);

        // The approver was the caller, who is also the draft creator here. info.md permits that
        // and requires it to be visible.
        Assert.True(Assert.Single(record.Approvals).ApproverWasDraftCreator);
    }

    [Fact]
    public async Task a_correction_sends_the_record_back_and_a_revision_supersedes_the_approval()
    {
        var harness = new Harness();
        KnowledgeRecord record = TestData.NewDraft();
        harness.Ports.Record = record;

        await harness.SucceedAsync(
            new SubmitForApprovalUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, record.Id));

        LifecycleResult corrected = await harness.SucceedAsync(
            new RequestCorrectionUseCase(harness.Ports, harness.Ports),
            new RequestCorrectionRequest(TestData.Scope, record.Id, "The rollback step is missing."));

        Assert.Equal(RecordStatus.Draft, corrected.Status);
        Assert.Equal("The rollback step is missing.", Assert.Single(record.CorrectionRequests).Reason);

        LifecycleResult revised = await harness.SucceedAsync(
            new ReviseDraftUseCase(harness.Ports, harness.Ports),
            new ReviseDraftRequest(
                TestData.Scope, record.Id, "Title", "Now with a rollback step.", TestData.Provenance));

        Assert.Equal(2, revised.CurrentRevisionNumber);
        Assert.Null(record.ApprovalForCurrentRevision);
    }

    [Fact]
    public async Task an_archived_record_refuses_further_lifecycle_operations()
    {
        var harness = new Harness();
        KnowledgeRecord record = Published(harness);
        harness.Ports.Record = record;

        await harness.SucceedAsync(
            new ArchiveRecordUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, record.Id));

        UseCaseResult<LifecycleResult> again = await harness.RunAsync(
            new ArchiveRecordUseCase(harness.Ports, harness.Ports),
            new RecordActionRequest(TestData.Scope, record.Id));

        Assert.Equal(ExecutionOutcome.Rejected, again.Outcome);
        Assert.Contains("archived", again.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static KnowledgeRecord Published(Harness harness, string publishedBody = "Approved text.")
    {
        KnowledgeRecord record = TestData.NewDraft(publishedBody);
        record.SubmitForApproval(TestData.Now);
        record.Approve(TestData.Reviewer, record.CurrentRevision.ContentHash, TestData.Now);
        record.Publish(TestData.Now);
        harness.Ports.Record = record;
        return record;
    }
}
