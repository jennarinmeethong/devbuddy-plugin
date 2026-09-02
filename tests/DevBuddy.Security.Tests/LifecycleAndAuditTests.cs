using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The controlled lifecycle end to end, and the audit trail it leaves behind.
/// <para>
/// Phase 11 scenario 5 lives here: a record goes from draft to published, and then the two ways
/// an approval can fail to mean what it says — approving content that has moved on, and approving
/// across a project boundary — are both refused and both recorded.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class LifecycleAndAuditTests(SecurityFixture fixture)
{
    private static readonly Provenance Source =
        new(ProvenanceSourceKind.HumanAuthored, "handover/2026-09-01", "Jennarin", World.Now);

    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_record_travels_from_draft_through_correction_to_published()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Reviewer);

        LifecycleResult draft = await stage.SucceedAsync(
            new CreateDraftUseCase(stage.Repository, stage.Clock),
            new CreateDraftRequest(
                stage.World.Alpha, stage.WorkItem.Id, RecordKind.Decision,
                "Why the import runs first", "First attempt.", Source));

        Assert.Equal(RecordStatus.Draft, draft.Status);
        Assert.Null(draft.PublishedRevisionNumber);

        await stage.SucceedAsync(
            new SubmitForApprovalUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, draft.RecordId));

        LifecycleResult corrected = await stage.SucceedAsync(
            new RequestCorrectionUseCase(stage.Repository, stage.Clock),
            new RequestCorrectionRequest(
                stage.World.Alpha, draft.RecordId, "The rollback step is missing."));

        Assert.Equal(RecordStatus.Draft, corrected.Status);

        LifecycleResult revised = await stage.SucceedAsync(
            new ReviseDraftUseCase(stage.Repository, stage.Clock),
            new ReviseDraftRequest(
                stage.World.Alpha, draft.RecordId, "Why the import runs first",
                "Second attempt, with a rollback step.", Source));

        Assert.Equal(2, revised.CurrentRevisionNumber);

        await stage.SucceedAsync(
            new SubmitForApprovalUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, draft.RecordId));

        ApprovalResult approval = await stage.SucceedAsync(
            new ApproveRecordUseCase(stage.Repository, stage.Clock),
            new ApproveRecordRequest(
                stage.World.Alpha, draft.RecordId, await stage.CurrentHashAsync(draft.RecordId)));

        Assert.Equal(2, approval.ApprovedRevisionNumber);

        LifecycleResult published = await stage.SucceedAsync(
            new PublishRecordUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, draft.RecordId));

        Assert.Equal(RecordStatus.Published, published.Status);
        Assert.Equal(2, published.PublishedRevisionNumber);

        // The correction and its reason survive into the stored record. The reason is why the
        // second revision exists, and a later owner needs it more than the diff.
        KnowledgeRecord stored = await stage.LoadAsync(draft.RecordId);
        Assert.Equal("The rollback step is missing.", Assert.Single(stored.CorrectionRequests).Reason);
        Assert.Equal("First attempt.", stored.Revisions[0].Body);
    }

    [Fact]
    public async Task publishing_with_an_approval_for_an_older_revision_is_rejected_and_audited()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Reviewer);
        KnowledgeRecordId recordId = await stage.ApprovedDraftAsync("Approved text.");

        // The text changes after the reviewer read it. This is the accident and the attack.
        await stage.SucceedAsync(
            new ReviseDraftUseCase(stage.Repository, stage.Clock),
            new ReviseDraftRequest(
                stage.World.Alpha, recordId, "Title", "Changed after approval.", Source));

        await stage.SucceedAsync(
            new SubmitForApprovalUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, recordId));

        UseCaseResult<LifecycleResult> refused = await stage.RunAsync(
            new PublishRecordUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, recordId));

        Assert.Equal(ExecutionOutcome.Rejected, refused.Outcome);

        // Nothing was published, and the earlier approval did not carry over.
        KnowledgeRecord stored = await stage.LoadAsync(recordId);
        Assert.Null(stored.PublishedRevisionNumber);
        Assert.Null(stored.ApprovalForCurrentRevision);

        AuditEvent rejection = await stage.LastAuditAsync(AuditAction.RecordPublished);
        Assert.Equal(AuditOutcome.Failed, rejection.Outcome);
        Assert.Contains(recordId.ToString(), rejection.ResourceReference, StringComparison.Ordinal);
        Assert.Contains("rejection", rejection.Details.Keys);
    }

    [Fact]
    public async Task approving_a_hash_that_is_no_longer_current_is_rejected_and_audited()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Reviewer);

        LifecycleResult draft = await stage.SucceedAsync(
            new CreateDraftUseCase(stage.Repository, stage.Clock),
            new CreateDraftRequest(
                stage.World.Alpha, stage.WorkItem.Id, RecordKind.Decision,
                "Title", "First text.", Source));

        string staleHash = await stage.CurrentHashAsync(draft.RecordId);

        await stage.SucceedAsync(
            new ReviseDraftUseCase(stage.Repository, stage.Clock),
            new ReviseDraftRequest(stage.World.Alpha, draft.RecordId, "Title", "Second text.", Source));

        await stage.SucceedAsync(
            new SubmitForApprovalUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, draft.RecordId));

        UseCaseResult<ApprovalResult> refused = await stage.RunAsync(
            new ApproveRecordUseCase(stage.Repository, stage.Clock),
            new ApproveRecordRequest(stage.World.Alpha, draft.RecordId, staleHash));

        // The reviewer is told to re-read rather than being quietly given what they asked for.
        Assert.Equal(ExecutionOutcome.Rejected, refused.Outcome);
        Assert.Contains("current revision", refused.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await stage.LoadAsync(draft.RecordId)).Approvals);

        AuditEvent rejection = await stage.LastAuditAsync(AuditAction.RecordApproved);
        Assert.Equal(AuditOutcome.Failed, rejection.Outcome);
    }

    [Fact]
    public async Task approving_a_record_in_another_project_is_denied_and_audited()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Reviewer);

        // A record in Beta, and a reviewer whose grant covers Alpha only.
        WorkItem betaItem = await _fixture.SeedWorkItemAsync(
            stage.World.Beta, "CRQ-X1", stage.World.Founder);

        KnowledgeRecord betaRecord = await _fixture.SeedPublishedRecordAsync(
            stage.World.Beta, betaItem.Id, "Beta", "Beta text.", stage.World.Founder);

        UseCaseResult<ApprovalResult> denied = await stage.RunAsync(
            new ApproveRecordUseCase(stage.Repository, stage.Clock),
            new ApproveRecordRequest(
                stage.World.Beta, betaRecord.Id, betaRecord.CurrentRevision.ContentHash.Value));

        Assert.Equal(ExecutionOutcome.Denied, denied.Outcome);

        // The denial is recorded against the project that was reached for, not the one the
        // caller can see. That is what lets the administrator of Beta find out that somebody
        // tried.
        AuditEvent denial = await stage.LastAuditAsync(AuditAction.AccessDenied, stage.World.Beta);
        Assert.Equal(AuditOutcome.Denied, denial.Outcome);
        Assert.Contains("approve_record", denial.ResourceReference, StringComparison.Ordinal);
        Assert.Equal(stage.World.BetaId, denial.ProjectId);
    }

    [Fact]
    public async Task the_audit_records_the_approver_the_exact_revision_and_whether_they_wrote_it()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Administrator);
        KnowledgeRecordId recordId = await stage.ApprovedDraftAsync("Self approved text.");

        AuditEvent entry = await stage.LastAuditAsync(AuditAction.RecordApproved);

        // The four things info.md requires the audit history to record.
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(stage.Actor.ToString(), entry.Details["approver"]);
        Assert.Equal("1", entry.Details["approved_revision"]);
        Assert.Equal(64, entry.Details["approved_content_hash"].Length);
        Assert.True(DateTimeOffset.TryParse(entry.Details["approved_at"], out _));

        // The approver here also wrote the draft. info.md permits that and requires it to be
        // visible, which means visible in the audit history rather than only on the record.
        Assert.Equal("true", entry.Details["approver_was_draft_creator"]);
    }

    [Fact]
    public async Task the_audit_records_which_revision_went_live()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Administrator);
        KnowledgeRecordId recordId = await stage.ApprovedDraftAsync("Body.");

        await stage.SucceedAsync(
            new PublishRecordUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, recordId));

        AuditEvent entry = await stage.LastAuditAsync(AuditAction.RecordPublished);

        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal("Published", entry.Details["status"]);
        Assert.Equal("1", entry.Details["published_revision"]);
    }

    [Fact]
    public async Task the_audit_trail_never_contains_the_content_it_describes()
    {
        const string Marker = "zephyrantine-marker-string";

        Stage stage = await Stage.CreateAsync(_fixture, Role.Administrator);
        KnowledgeRecordId recordId = await stage.ApprovedDraftAsync($"Body containing {Marker}.");

        await stage.SucceedAsync(
            new PublishRecordUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, recordId));

        await stage.SucceedAsync(
            new GetRecordUseCase(stage.Repository),
            new GetRecordRequest(stage.World.Alpha, recordId));

        await stage.SucceedAsync(
            new SearchKnowledgeUseCase(stage.Session.Resolve<ISearchIndex>()),
            new SearchKnowledgeRequest(stage.World.Alpha, "zephyrantine"));

        string pattern = $"%{Marker}%";

        // Sanity first: the marker really is in the record. Without this the test could pass
        // because nothing ever wrote it.
        int inContent = await stage.Session.Db.Database
            .SqlQuery<int>($"select count(*)::int as \"Value\" from record_revisions where body like {pattern}")
            .SingleAsync();

        Assert.True(inContent > 0);

        // Every audit row in the database, resource reference and detail column alike, not only
        // the ones this test expects. Control SB-19: the audit store records that access
        // happened, never the payload it touched.
        int inAudit = await stage.Session.Db.Database
            .SqlQuery<int>($@"
                select count(*)::int as ""Value"" from audit_events
                where resource_reference like {pattern} or details::text like {pattern}")
            .SingleAsync();

        Assert.Equal(0, inAudit);
    }

    [Fact]
    public async Task a_reader_of_published_knowledge_never_sees_the_draft_that_follows_it()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Reviewer);
        KnowledgeRecordId recordId = await stage.ApprovedDraftAsync("Approved and published text.");

        await stage.SucceedAsync(
            new PublishRecordUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, recordId));

        await stage.SucceedAsync(
            new ReviseDraftUseCase(stage.Repository, stage.Clock),
            new ReviseDraftRequest(
                stage.World.Alpha, recordId, "Title", "Unreviewed follow-up text.", Source));

        KnowledgeRecordView view = await stage.SucceedAsync(
            new GetRecordUseCase(stage.Repository),
            new GetRecordRequest(stage.World.Alpha, recordId));

        // Control SB-26. A caller who did not ask for a revision gets the published one.
        Assert.Equal("Approved and published text.", view.Body);
        Assert.Equal(1, view.PublishedRevisionNumber);

        SearchKnowledgeResponse published = await stage.SucceedAsync(
            new SearchKnowledgeUseCase(stage.Session.Resolve<ISearchIndex>()),
            new SearchKnowledgeRequest(
                stage.World.Alpha, "follow-up", Statuses: [RecordStatus.Published]));

        Assert.Empty(published.Hits);

        // The history shows both, and says which one readers actually see.
        RecordHistoryResponse history = await stage.SucceedAsync(
            new ViewRecordHistoryUseCase(stage.Repository),
            new ViewRecordHistoryRequest(stage.World.Alpha, recordId));

        Assert.Equal(2, history.Revisions.Count);
        Assert.True(history.Revisions[0].IsPublished);
        Assert.False(history.Revisions[1].IsPublished);
        Assert.NotNull(history.Revisions[0].Approval);
        Assert.Null(history.Revisions[1].Approval);
    }

    [Fact]
    public async Task an_archived_record_refuses_further_changes_and_the_refusal_is_audited()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Reviewer);
        KnowledgeRecordId recordId = await stage.ApprovedDraftAsync("Body.");

        await stage.SucceedAsync(
            new PublishRecordUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, recordId));

        await stage.SucceedAsync(
            new ArchiveRecordUseCase(stage.Repository, stage.Clock),
            new RecordActionRequest(stage.World.Alpha, recordId));

        UseCaseResult<LifecycleResult> refused = await stage.RunAsync(
            new ReviseDraftUseCase(stage.Repository, stage.Clock),
            new ReviseDraftRequest(stage.World.Alpha, recordId, "Title", "Too late.", Source));

        Assert.Equal(ExecutionOutcome.Rejected, refused.Outcome);
        Assert.Equal(AuditOutcome.Failed, (await stage.LastAuditAsync(AuditAction.RevisionAdded)).Outcome);
    }

    [Fact]
    public async Task an_administrator_can_read_the_audit_trail_for_their_project_only()
    {
        Stage stage = await Stage.CreateAsync(_fixture, Role.Administrator);
        KnowledgeRecordId recordId = await stage.ApprovedDraftAsync("Body.");

        var useCase = new ReadAuditHistoryUseCase(stage.Session.Resolve<IAuditReader>());

        AuditHistoryResponse alpha = await stage.SucceedAsync(
            useCase,
            new ReadAuditHistoryRequest(
                stage.World.Alpha, World.Now.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));

        Assert.Contains(alpha.Entries, entry => entry.Action == AuditAction.RecordApproved);
        Assert.All(alpha.Entries, entry => Assert.Equal(stage.World.AlphaId, entry.ProjectId));

        // The grant is on Alpha. Reading Beta audit is a different question with a different
        // answer, even for an administrator.
        Assert.Equal(ExecutionOutcome.Denied, (await stage.RunAsync(
            useCase,
            new ReadAuditHistoryRequest(
                stage.World.Beta, World.Now.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))).Outcome);
    }

    /// <summary>
    /// A world, a signed-in actor with a role on Alpha, a work item, and an open session. Every
    /// test here needs all five, and repeating them would bury what each test is actually about.
    /// </summary>
    private sealed class Stage
    {
        private Stage(SecurityFixture fixture, World world, Session session, UserId actor, WorkItem workItem)
        {
            Fixture = fixture;
            World = world;
            Session = session;
            Actor = actor;
            WorkItem = workItem;
        }

        public SecurityFixture Fixture { get; }

        public World World { get; }

        public Session Session { get; }

        public UserId Actor { get; }

        public WorkItem WorkItem { get; }

        public CallerContext Caller => World.Human(Actor);

        public IKnowledgeRepository Repository => Session.Resolve<IKnowledgeRepository>();

        public IClock Clock => Session.Resolve<IClock>();

        public static async Task<Stage> CreateAsync(SecurityFixture fixture, Role role)
        {
            World world = await fixture.CreateWorldAsync();
            UserId actor = await fixture.CreateUserAsync($"stage-{Guid.NewGuid():N}@example.com");
            await fixture.GrantAsync(world.Workspace, actor, role, world.AlphaId);

            WorkItem item = await fixture.SeedWorkItemAsync(world.Alpha, "CRQ-L1", world.Founder);
            return new Stage(fixture, world, fixture.OpenSession(world.Workspace), actor, item);
        }

        public Task<UseCaseResult<TResponse>> RunAsync<TRequest, TResponse>(
            UseCase<TRequest, TResponse> useCase, TRequest request)
            where TRequest : IUseCaseRequest =>
            Session.RunAsync(useCase, request, Caller);

        public async Task<TResponse> SucceedAsync<TRequest, TResponse>(
            UseCase<TRequest, TResponse> useCase, TRequest request)
            where TRequest : IUseCaseRequest
        {
            UseCaseResult<TResponse> result = await RunAsync(useCase, request);

            Assert.True(
                result.IsSuccess,
                $"Expected success but got {result.Outcome}: {result.Reason} "
                + string.Join("; ", result.ValidationErrors));

            return result.Value!;
        }

        /// <summary>A record created, submitted, and approved by the actor.</summary>
        public async Task<KnowledgeRecordId> ApprovedDraftAsync(string body)
        {
            LifecycleResult draft = await SucceedAsync(
                new CreateDraftUseCase(Repository, Clock),
                new CreateDraftRequest(
                    World.Alpha, WorkItem.Id, RecordKind.Decision, "Title", body, Source));

            await SucceedAsync(
                new SubmitForApprovalUseCase(Repository, Clock),
                new RecordActionRequest(World.Alpha, draft.RecordId));

            await SucceedAsync(
                new ApproveRecordUseCase(Repository, Clock),
                new ApproveRecordRequest(
                    World.Alpha, draft.RecordId, await CurrentHashAsync(draft.RecordId)));

            return draft.RecordId;
        }

        public async Task<KnowledgeRecord> LoadAsync(KnowledgeRecordId recordId) =>
            await Repository.FindRecordAsync(recordId, World.Alpha, CancellationToken.None)
            ?? throw new InvalidOperationException($"Record {recordId} was not found.");

        public async Task<string> CurrentHashAsync(KnowledgeRecordId recordId) =>
            (await LoadAsync(recordId)).CurrentRevision.ContentHash.Value;

        /// <summary>
        /// The newest audit entry for one action, read straight from the store rather than
        /// through the pipeline, so a test can inspect what was recorded about a request the
        /// caller was not allowed to make.
        /// </summary>
        public async Task<AuditEvent> LastAuditAsync(
            AuditAction action, Domain.Tenancy.ProjectScope? scope = null)
        {
            IReadOnlyList<AuditEvent> entries = await Session.Resolve<IAuditReader>()
                .QueryAsync(
                    scope ?? World.Alpha,
                    World.Now.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(1),
                    actorId: null,
                    CancellationToken.None);

            return entries.FirstOrDefault(entry => entry.Action == action)
                ?? throw new InvalidOperationException($"No audit entry for {action}.");
        }
    }
}
