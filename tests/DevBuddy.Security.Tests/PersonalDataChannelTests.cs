using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Control SB-18 through the real pipeline: personal data denied by default on the AI channel,
/// unless the project owner has separately approved a bounded data-sharing scope — and even then,
/// the prohibition on secrets still applies (info.md).
/// <para>
/// <c>RetentionAndEgressTests</c> covers SB-17, which has no such exception and applies to every
/// channel. This file is only about the parts that differ: which channel, and whether a bounded
/// scope was approved.
/// </para>
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class PersonalDataChannelTests(SecurityFixture fixture)
{
    private static readonly Provenance Source =
        new(ProvenanceSourceKind.HumanAuthored, "handover/2026-09-01", "Jennarin", World.Now);

    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_human_can_draft_personal_data_on_their_own_project()
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        // No bounded scope approved. A person working their own project data is ordinary use,
        // which SB-18 has nothing to say about — only the AI channel is gated.
        LifecycleResult draft = await ground.SucceedAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Customer escalation", "Customer SSN on file: 123-45-6789.", Source),
            World.Human(ground.Actor));

        Assert.Equal(RecordStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task the_ai_channel_is_refused_personal_data_when_no_bounded_scope_is_approved()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        UseCaseResult<LifecycleResult> blocked = await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Customer escalation", "Customer SSN on file: 123-45-6789.", Source),
            World.Ai(ground.Actor));

        Assert.Equal(ExecutionOutcome.Blocked, blocked.Outcome);
        Assert.Contains("personal-data:us-ssn", string.Join(" ", blocked.ValidationErrors), StringComparison.Ordinal);

        int stored = await ground.Session.Db.KnowledgeRecords
            .CountAsync(record => record.ProjectId == ground.World.AlphaId.Value);
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task an_approved_bounded_scope_lets_the_ai_channel_draft_personal_data()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(
            ground.World.Alpha, ground.World.Founder, boundedDataScope: "support-escalations");

        LifecycleResult draft = await ground.SucceedAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Customer escalation", "Customer SSN on file: 123-45-6789.", Source),
            World.Ai(ground.Actor));

        Assert.Equal(RecordStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task a_bounded_scope_does_not_excuse_a_secret_on_the_ai_channel()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(
            ground.World.Alpha, ground.World.Founder, boundedDataScope: "support-escalations");

        UseCaseResult<LifecycleResult> blocked = await ground.RunAsync(
            new CreateDraftUseCase(ground.Repository, ground.Clock),
            new CreateDraftRequest(
                ground.World.Alpha, ground.WorkItem.Id, RecordKind.Decision,
                "Customer escalation", "Connect with AKIAIOSFODNN7EXAMPLE and then run the migration.", Source),
            World.Ai(ground.Actor));

        Assert.Equal(ExecutionOutcome.Blocked, blocked.Outcome);
        Assert.Contains("aws-access-key-id", string.Join(" ", blocked.ValidationErrors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_read_over_the_ai_channel_redacts_personal_data_when_no_bounded_scope_is_approved()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(ground.World.Alpha, ground.World.Founder);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), ground.World.Alpha, ground.WorkItem.Id, RecordKind.TechnicalKnowledge,
            "Legacy escalation", "Customer date of birth: 1990-04-12, recorded for verification.",
            frontMatter: null, Source, World.Now, ground.Actor);

        await ground.Repository.AddRecordAsync(record, CancellationToken.None);

        KnowledgeRecordView view = await ground.SucceedAsync(
            new GetRecordUseCase(ground.Repository),
            new GetRecordRequest(ground.World.Alpha, record.Id),
            World.Ai(ground.Actor));

        Assert.DoesNotContain("1990-04-12", view.Body, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", view.Body, StringComparison.Ordinal);

        // The label survives; only the value is removed.
        Assert.Contains("date of birth", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_read_over_the_ai_channel_is_not_redacted_when_a_bounded_scope_is_approved()
    {
        Ground ground = await Ground.CreateAsync(_fixture);
        await _fixture.EnableAiAccessAsync(
            ground.World.Alpha, ground.World.Founder, boundedDataScope: "support-escalations");

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), ground.World.Alpha, ground.WorkItem.Id, RecordKind.TechnicalKnowledge,
            "Legacy escalation", "Customer date of birth: 1990-04-12, recorded for verification.",
            frontMatter: null, Source, World.Now, ground.Actor);

        await ground.Repository.AddRecordAsync(record, CancellationToken.None);

        KnowledgeRecordView view = await ground.SucceedAsync(
            new GetRecordUseCase(ground.Repository),
            new GetRecordRequest(ground.World.Alpha, record.Id),
            World.Ai(ground.Actor));

        Assert.Contains("1990-04-12", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_human_read_is_never_redacted_for_personal_data()
    {
        Ground ground = await Ground.CreateAsync(_fixture);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), ground.World.Alpha, ground.WorkItem.Id, RecordKind.TechnicalKnowledge,
            "Legacy escalation", "Customer date of birth: 1990-04-12, recorded for verification.",
            frontMatter: null, Source, World.Now, ground.Actor);

        await ground.Repository.AddRecordAsync(record, CancellationToken.None);

        KnowledgeRecordView view = await ground.SucceedAsync(
            new GetRecordUseCase(ground.Repository),
            new GetRecordRequest(ground.World.Alpha, record.Id),
            World.Human(ground.Actor));

        Assert.Contains("1990-04-12", view.Body, StringComparison.Ordinal);
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
            UserId actor = await fixture.CreateUserAsync($"pii-{Guid.NewGuid():N}@example.com");
            await fixture.GrantAsync(world.Workspace, actor, Role.Reviewer, world.AlphaId);

            WorkItem item = await fixture.SeedWorkItemAsync(world.Alpha, "CRQ-PII1", world.Founder);
            return new Ground(world, fixture.OpenSession(world.Workspace), actor, item);
        }

        public Task<UseCaseResult<TResponse>> RunAsync<TRequest, TResponse>(
            UseCase<TRequest, TResponse> useCase, TRequest request, CallerContext caller)
            where TRequest : IUseCaseRequest =>
            Session.RunAsync(useCase, request, caller);

        public async Task<TResponse> SucceedAsync<TRequest, TResponse>(
            UseCase<TRequest, TResponse> useCase, TRequest request, CallerContext caller)
            where TRequest : IUseCaseRequest
        {
            UseCaseResult<TResponse> result = await RunAsync(useCase, request, caller);

            Assert.True(
                result.IsSuccess,
                $"Expected success but got {result.Outcome}: {result.Reason} "
                + string.Join("; ", result.ValidationErrors));

            return result.Value!;
        }
    }
}
