using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Security.Tests;

/// <summary>
/// What a person copied out of a workspace (Phase 13, D8), read from the real audit trail over real
/// PostgreSQL. The copies themselves cannot be recalled; the point is that an administrator
/// revoking access can see what left, in their own workspace and nowhere else.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class MemberDownloadsTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_administrator_sees_what_a_member_downloaded_and_exported_here_and_nothing_else()
    {
        World ours = await _fixture.CreateWorldAsync();
        World elsewhere = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"dl-admin-{Guid.NewGuid():N}@example.com");
        UserId member = await _fixture.CreateUserAsync($"dl-member-{Guid.NewGuid():N}@example.com");
        UserId someoneElse = await _fixture.CreateUserAsync($"dl-other-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(ours.Workspace, administrator, Role.Administrator);
        await _fixture.GrantAsync(ours.Workspace, member, Role.Contributor);

        DateTimeOffset recent = DateTimeOffset.UtcNow.AddDays(-1);

        await WriteAsync(ours.Alpha, member, AuditAction.EvidenceDownloaded, AuditOutcome.Succeeded, "download_evidence:e1", recent);
        await WriteAsync(ours.Beta, member, AuditAction.ExportCreated, AuditOutcome.Succeeded, "export_project:x1", recent);

        // None of these may appear: a refused download, an ordinary read, somebody else's
        // download, the member's download in another workspace, and one outside the window.
        await WriteAsync(ours.Alpha, member, AuditAction.EvidenceDownloaded, AuditOutcome.Denied, "download_evidence:e2", recent);
        await WriteAsync(ours.Alpha, member, AuditAction.RecordViewed, AuditOutcome.Succeeded, "get_record:r1", recent);
        await WriteAsync(ours.Alpha, someoneElse, AuditAction.EvidenceDownloaded, AuditOutcome.Succeeded, "download_evidence:e3", recent);
        await WriteAsync(elsewhere.Alpha, member, AuditAction.EvidenceDownloaded, AuditOutcome.Succeeded, "download_evidence:e4", recent);
        await WriteAsync(ours.Alpha, member, AuditAction.EvidenceDownloaded, AuditOutcome.Succeeded, "download_evidence:e5", DateTimeOffset.UtcNow.AddDays(-100));

        UseCaseResult<MemberDownloadsResponse> result = await ListAsync(ours.Workspace, administrator, member);

        Assert.True(result.IsSuccess, result.Reason);
        Assert.Equal(
            ["download_evidence:e1", "export_project:x1"],
            result.Value!.Downloads.Select(download => download.ResourceReference).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task a_member_without_the_audit_permission_is_refused()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"dl-rev-{Guid.NewGuid():N}@example.com");
        UserId member = await _fixture.CreateUserAsync($"dl-m-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Reviewer);
        await _fixture.GrantAsync(world.Workspace, member, Role.Viewer);

        Assert.Equal(ExecutionOutcome.Denied, (await ListAsync(world.Workspace, reviewer, member)).Outcome);
    }

    private async Task WriteAsync(
        ProjectScope scope, UserId actor, AuditAction action, AuditOutcome outcome, string reference, DateTimeOffset at)
    {
        using Session session = _fixture.OpenSession(scope.WorkspaceId);

        await session.Resolve<IAuditSink>().WriteAsync(
            AuditEvent.ForProject(AuditEventId.New(), scope, actor, AuditChannel.Human, action, outcome, reference, at),
            TestToken.None);
    }

    private async Task<UseCaseResult<MemberDownloadsResponse>> ListAsync(WorkspaceId workspace, UserId caller, UserId subject)
    {
        using Session session = _fixture.OpenSession(workspace);
        var useCase = new ListMemberDownloadsUseCase(session.Resolve<IAuditReader>(), session.Resolve<IClock>());

        return await session.RunAsync(useCase, new ListMemberDownloadsRequest(workspace, subject), World.Human(caller));
    }
}
