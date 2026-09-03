using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Closing one of the v1 gaps named in <c>docs/security/release-readiness.md</c>: a project can
/// now actually be deleted, and deleting it removes everything scoped to it — work items, records
/// and their revisions, evidence rows and bytes, and project-scoped memberships — without
/// touching the project next to it.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class ProjectDeletionTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task deleting_a_project_removes_its_records_work_items_and_evidence_bytes()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"del-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-DEL1", world.Founder);
        KnowledgeRecord record = await _fixture.SeedPublishedRecordAsync(
            world.Alpha, item.Id, "Doomed record", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        EvidenceObject evidence;

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("evidence bytes")))
        {
            evidence = await session.Resolve<IEvidenceStore>()
                .StoreAsync(world.Alpha, stream, "text/plain", world.Founder, CancellationToken.None);
        }

        await DeleteAsync(session, world.Alpha, administrator);

        Assert.False(await session.Db.KnowledgeRecords
            .IgnoreQueryFilters().AnyAsync(row => row.Id == record.Id.Value));
        Assert.False(await session.Db.WorkItems
            .IgnoreQueryFilters().AnyAsync(row => row.Id == item.Id.Value));
        Assert.False(await session.Db.EvidenceObjects
            .IgnoreQueryFilters().AnyAsync(row => row.Id == evidence.Id.Value));
        Assert.False(await session.Db.Projects
            .IgnoreQueryFilters().AnyAsync(row => row.Id == world.AlphaId.Value));

        // The bytes, not only the row.
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            session.Resolve<IEvidenceStore>().OpenReadAsync(evidence, CancellationToken.None));
    }

    [Fact]
    public async Task deleting_a_project_does_not_touch_the_one_next_to_it()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"del-iso-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator, world.AlphaId);

        WorkItem betaItem = await _fixture.SeedWorkItemAsync(world.Beta, "CRQ-DEL2", world.Founder);
        KnowledgeRecord betaRecord = await _fixture.SeedPublishedRecordAsync(
            world.Beta, betaItem.Id, "Beta survives", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        await DeleteAsync(session, world.Alpha, administrator);

        Assert.True(await session.Db.KnowledgeRecords
            .IgnoreQueryFilters().AnyAsync(row => row.Id == betaRecord.Id.Value));
        Assert.True(await session.Db.WorkItems
            .IgnoreQueryFilters().AnyAsync(row => row.Id == betaItem.Id.Value));
        Assert.True(await session.Db.Projects
            .IgnoreQueryFilters().AnyAsync(row => row.Id == world.BetaId.Value));
    }

    [Fact]
    public async Task audit_history_survives_the_project_it_describes()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"del-audit-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator, world.AlphaId);

        WorkItem item = await _fixture.SeedWorkItemAsync(world.Alpha, "CRQ-DEL3", world.Founder);
        await _fixture.SeedPublishedRecordAsync(world.Alpha, item.Id, "Title", "Body.", world.Founder);

        using Session session = _fixture.OpenSession(world.Workspace);

        await DeleteAsync(session, world.Alpha, administrator);

        // Deleting the project removes its content; it does not erase that the deletion happened.
        var deletion = await session.Db.AuditEvents
            .SingleAsync(entry => entry.ProjectId == world.AlphaId.Value
                && entry.Action == (int)AuditAction.ProjectDeleted);

        Assert.Equal(administrator.Value, deletion.ActorId);
        Assert.Equal((int)AuditOutcome.Succeeded, deletion.Outcome);
    }

    [Fact]
    public async Task a_reviewer_cannot_delete_a_project()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"del-denied-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Reviewer, world.AlphaId);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<ProjectDeletedResponse> result = await session.RunAsync(
            new DeleteProjectUseCase(session.Resolve<IProjectDirectory>()),
            new DeleteProjectRequest(world.Alpha),
            World.Human(reviewer));

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);

        Assert.True(await session.Db.Projects
            .IgnoreQueryFilters().AnyAsync(row => row.Id == world.AlphaId.Value));
    }

    private static async Task DeleteAsync(Session session, ProjectScope scope, UserId administrator)
    {
        UseCaseResult<ProjectDeletedResponse> result = await session.RunAsync(
            new DeleteProjectUseCase(session.Resolve<IProjectDirectory>()),
            new DeleteProjectRequest(scope),
            World.Human(administrator));

        Assert.True(result.IsSuccess, $"Expected success but got {result.Outcome}: {result.Reason}");
    }
}
