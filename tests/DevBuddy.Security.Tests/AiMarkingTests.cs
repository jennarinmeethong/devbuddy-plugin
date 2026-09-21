using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Work;

namespace DevBuddy.Security.Tests;

/// <summary>
/// An administrator recording, after the fact, that an AI wrote a record (Phase 13, D3), through
/// the real pipeline over real PostgreSQL. What matters: the mark goes one way, says who and why,
/// and leaves the content and the approval that binds it exactly as they were.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class AiMarkingTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_administrator_marks_a_published_record_and_its_content_and_approval_are_untouched()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"marker-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);
        KnowledgeRecord record = await SeedAsync(world);
        ContentHash before = record.CurrentRevision.ContentHash;

        UseCaseResult<RecordMarkedAiGeneratedResponse> result =
            await Mark(world, administrator, record.Id, "The author confirmed an assistant wrote the whole draft.");

        Assert.True(result.IsSuccess, result.Reason);
        Assert.Equal(1, result.Value!.RevisionsMarked);

        KnowledgeRecord reloaded = await ReloadAsync(world, record.Id);
        Provenance provenance = reloaded.CurrentRevision.Provenance;

        Assert.True(provenance.IsAiGenerated);
        Assert.NotNull(provenance.AiMarking);
        Assert.Equal(administrator, provenance.AiMarking!.MarkedBy);
        Assert.Equal("The author confirmed an assistant wrote the whole draft.", provenance.AiMarking.Reason);

        // Content, hash, publication and approval are what they were.
        Assert.Equal(before, reloaded.CurrentRevision.ContentHash);
        Assert.Equal(record.CurrentRevision.Body, reloaded.CurrentRevision.Body);
        Assert.Equal(RecordStatus.Published, reloaded.Status);
        Assert.NotNull(reloaded.ApprovalForCurrentRevision);
    }

    [Fact]
    public async Task the_mark_goes_one_way_and_is_not_overwritten()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId first = await _fixture.CreateUserAsync($"marker1-{Guid.NewGuid():N}@example.com");
        UserId second = await _fixture.CreateUserAsync($"marker2-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, first, Role.Administrator);
        await _fixture.GrantAsync(world.Workspace, second, Role.Administrator);
        KnowledgeRecord record = await SeedAsync(world);

        Assert.True((await Mark(world, first, record.Id, "First.")).IsSuccess);

        UseCaseResult<RecordMarkedAiGeneratedResponse> again = await Mark(world, second, record.Id, "Second.");

        Assert.Equal(ExecutionOutcome.Rejected, again.Outcome);
        Assert.Equal(first, (await ReloadAsync(world, record.Id)).CurrentRevision.Provenance.AiMarking!.MarkedBy);
    }

    [Fact]
    public async Task a_reviewer_cannot_mark_and_a_reason_is_required()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"marker-rev-{Guid.NewGuid():N}@example.com");
        UserId administrator = await _fixture.CreateUserAsync($"marker-adm-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Reviewer);
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);
        KnowledgeRecord record = await SeedAsync(world);

        Assert.Equal(ExecutionOutcome.Denied, (await Mark(world, reviewer, record.Id, "I think so.")).Outcome);
        Assert.Equal(ExecutionOutcome.Invalid, (await Mark(world, administrator, record.Id, "   ")).Outcome);
        Assert.False((await ReloadAsync(world, record.Id)).CurrentRevision.Provenance.IsAiGenerated);
    }

    [Fact]
    public async Task a_record_the_channel_already_marked_is_refused()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"marker-ch-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);
        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, $"CRQ-AI{Guid.NewGuid():N}"[..12], world.Founder);

        KnowledgeRecord record = KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), world.Alpha, item.Id, RecordKind.Decision, "AI draft", "Body.",
            frontMatter: null,
            new Provenance(ProvenanceSourceKind.RepositoryAnalysis, "seed", "assistant", World.Now, isAiGenerated: true),
            World.Now, world.Founder);

        using (Session session = _fixture.OpenSession(world.Workspace))
        {
            await session.Resolve<IKnowledgeRepository>().AddRecordAsync(record, TestToken.None);
        }

        Assert.Equal(ExecutionOutcome.Rejected, (await Mark(world, administrator, record.Id, "Already known.")).Outcome);
        Assert.Null((await ReloadAsync(world, record.Id)).CurrentRevision.Provenance.AiMarking);
    }

    private async Task<KnowledgeRecord> SeedAsync(World world)
    {
        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, $"CRQ-M{Guid.NewGuid():N}"[..12], world.Founder);
        return await _fixture.SeedPublishedRecordAsync(world.Alpha, item.Id, "Written before the fix", "Body.", world.Founder);
    }

    private async Task<UseCaseResult<RecordMarkedAiGeneratedResponse>> Mark(
        World world, UserId caller, KnowledgeRecordId recordId, string reason)
    {
        using Session session = _fixture.OpenSession(world.Workspace);
        var useCase = new MarkRecordAiGeneratedUseCase(
            session.Resolve<IKnowledgeRepository>(), session.Resolve<IClock>());

        return await session.RunAsync(
            useCase, new MarkRecordAiGeneratedRequest(world.Alpha, recordId, reason), World.Human(caller));
    }

    private async Task<KnowledgeRecord> ReloadAsync(World world, KnowledgeRecordId recordId)
    {
        using Session session = _fixture.OpenSession(world.Workspace);

        return await session.Resolve<IKnowledgeRepository>().FindRecordAsync(recordId, world.Alpha, TestToken.None)
            ?? throw new InvalidOperationException("The record is gone.");
    }
}
