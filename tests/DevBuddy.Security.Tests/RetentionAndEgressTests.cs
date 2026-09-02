using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
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
/// Control SB-17 through the real pipeline, at both points info.md names: before material is
/// retained, and before a response leaves the boundary.
/// <para>
/// The two halves behave differently on purpose. Outbound text is redacted, because a reader loses
/// nothing they were entitled to. Inbound text is refused, because silently storing something
/// other than what the author wrote, without telling them, is worse than saying no.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class RetentionAndEgressTests(SecurityFixture fixture)
{
    private static readonly Provenance Source =
        new(ProvenanceSourceKind.HumanAuthored, "handover/2026-09-01", "Jennarin", World.Now);

    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_draft_carrying_a_credential_is_refused_and_nothing_is_stored()
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        UseCaseResult<LifecycleResult> blocked = await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha,
                ground.WorkItem.Id,
                RecordKind.TechnicalKnowledge,
                "Deployment notes",
                "Connect with AKIAIOSFODNN7EXAMPLE and then run the migration.",
                Source));

        Assert.Equal(ExecutionOutcome.Blocked, blocked.Outcome);
        Assert.Contains("aws-access-key-id", string.Join(" ", blocked.ValidationErrors), StringComparison.Ordinal);

        // Refused before the use case ran, so nothing reached the database.
        int stored = await ground.Session.Db.KnowledgeRecords
            .CountAsync(record => record.ProjectId == ground.World.AlphaId.Value);

        Assert.Equal(0, stored);
    }

    [Theory]
    [InlineData("password: hunter2-and-then-some")]
    [InlineData("ghp_1234567890abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("Server=db.internal;Database=x;Password=s3cr3t-p4ssw0rd;")]
    public async Task every_field_that_would_be_written_down_is_scanned(string secret)
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        // Title, body, front matter, and the provenance locator in turn. A scan that covered only
        // the body would leave three places to paste a credential into.
        Assert.Equal(ExecutionOutcome.Blocked, (await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                secret, "Body.", Source))).Outcome);

        Assert.Equal(ExecutionOutcome.Blocked, (await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Title", secret, Source))).Outcome);

        Assert.Equal(ExecutionOutcome.Blocked, (await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Title", "Body.", Source,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = secret }))).Outcome);

        Assert.Equal(ExecutionOutcome.Blocked, (await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision, "Title", "Body.",
                new Provenance(ProvenanceSourceKind.HumanAuthored, secret, "Jennarin", World.Now)))).Outcome);
    }

    [Fact]
    public async Task a_correction_reason_is_scanned_too_because_it_is_retained()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        KnowledgeRecordId recordId = await ground.DraftAsync("Ordinary text.");

        await ground.SucceedAsync(
            new SubmitForApprovalUseCase(ground.Repository, ground.Clock),
            new RecordActionRequest(ground.World.Alpha, recordId));

        UseCaseResult<LifecycleResult> blocked = await ground.RunAsync(
            new RequestCorrectionUseCase(ground.Repository, ground.Clock),
            new RequestCorrectionRequest(
                ground.World.Alpha, recordId, "Use password: hunter2-and-then-some instead."));

        Assert.Equal(ExecutionOutcome.Blocked, blocked.Outcome);
    }

    [Fact]
    public async Task a_block_is_audited_with_the_rule_that_matched_and_never_the_value()
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Title", "AKIAIOSFODNN7EXAMPLE", Source));

        IReadOnlyList<AuditEvent> entries = await ground.Session.Resolve<IAuditReader>()
            .QueryAsync(
                ground.World.Alpha, World.Now.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1),
                actorId: null, CancellationToken.None);

        AuditEvent entry = Assert.Single(entries, candidate => candidate.Action == AuditAction.ContentScanned);

        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Contains("aws-access-key-id", entry.Details["blocked_findings"], StringComparison.Ordinal);

        // The rule name and the line. Never the credential, in the audit any more than in the
        // result (SB-19).
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", entry.Details["blocked_findings"], StringComparison.Ordinal);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", entry.ResourceReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ordinary_content_is_not_blocked()
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        // The control has to be usable. A scanner that refused normal engineering prose would be
        // routed around within a week.
        LifecycleResult draft = await ground.SucceedAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Why the import runs first",
                "The importer normalises identifiers before the validator sees them. "
                + "See src/core/DevBuddy.Application for the pipeline, and commit e10fc43.",
                Source));

        Assert.Equal(RecordStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task a_secret_already_in_the_database_is_redacted_on_the_way_out()
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        // Written directly, bypassing the pipeline, the way material imported before the scanner
        // existed would have arrived.
        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), ground.World.Alpha, ground.WorkItem.Id, RecordKind.TechnicalKnowledge,
            "Legacy notes", "The old key was AKIAIOSFODNN7EXAMPLE, since rotated.",
            frontMatter: null, Source, World.Now, ground.Actor);

        await ground.Repository.AddRecordAsync(record, CancellationToken.None);

        KnowledgeRecordView view = await ground.SucceedAsync(
            new GetRecordUseCase(ground.Repository),
            new GetRecordRequest(ground.World.Alpha, record.Id));

        // The egress half catches what the retention half never saw.
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", view.Body, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task search_results_are_redacted_before_they_reach_a_caller()
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), ground.World.Alpha, ground.WorkItem.Id, RecordKind.TechnicalKnowledge,
            "Rotation runbook", "Superseded credential ghp_1234567890abcdefghijklmnopqrstuvwxyzAB removed.",
            frontMatter: null, Source, World.Now, ground.Actor);

        await ground.Repository.AddRecordAsync(record, CancellationToken.None);

        SearchKnowledgeResponse response = await ground.SucceedAsync(
            new SearchKnowledgeUseCase(ground.Session.Resolve<ISearchIndex>()),
            new SearchKnowledgeRequest(ground.World.Alpha, "rotation"));

        // Snippets are content too, and the snippet is the part a search result actually shows.
        Assert.All(response.Hits, hit =>
            Assert.DoesNotContain("ghp_1234567890", hit.Snippet, StringComparison.Ordinal));
    }

    private sealed class Ground
    {
        private Ground(World world, Session session, UserId actor, WorkItem workItem)
        {
            World = world;
            Session = session;
            Actor = actor;
            WorkItem = workItem;
        }

        public World World { get; }

        public Session Session { get; }

        public UserId Actor { get; }

        public WorkItem WorkItem { get; }

        public IKnowledgeRepository Repository => Session.Resolve<IKnowledgeRepository>();

        public IClock Clock => Session.Resolve<IClock>();

        public static async Task<Ground> CreateAsync(SecurityFixture fixture)
        {
            World world = await fixture.CreateWorldAsync();
            UserId actor = await fixture.CreateUserAsync($"scan-{Guid.NewGuid():N}@example.com");
            await fixture.GrantAsync(world.Workspace, actor, Role.Reviewer, world.AlphaId);

            WorkItem item = await fixture.SeedWorkItemAsync(world.Alpha, "CRQ-S1", world.Founder);
            return new Ground(world, fixture.OpenSession(world.Workspace), actor, item);
        }

        public Task<UseCaseResult<TResponse>> RunAsync<TRequest, TResponse>(
            UseCase<TRequest, TResponse> useCase, TRequest request)
            where TRequest : IUseCaseRequest =>
            Session.RunAsync(useCase, request, World.Human(Actor));

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

        public async Task<KnowledgeRecordId> DraftAsync(string body)
        {
            LifecycleResult draft = await SucceedAsync(
                new CreateDraftUseCase(Repository, Clock),
                new CreateDraftRequest(
                    World.Alpha, WorkItem.Id, RecordKind.Decision, "Title", body, Source));

            return draft.RecordId;
        }
    }
}
