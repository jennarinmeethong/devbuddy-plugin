using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// A password reset an administrator issues (Phase 13, D1), through the real pipeline over real
/// PostgreSQL. The case that matters most is the cross-tenant one: a password works everywhere,
/// so an administrator of one workspace must not be able to reset somebody who can also sign in
/// to a workspace they do not administer.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class PasswordResetTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_administrator_can_reset_a_member_who_then_sets_a_password_with_it()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId admin = await Person("admin");
        UserId member = await Person("member");
        await _fixture.GrantAsync(world.Workspace, admin, Role.Administrator);
        await _fixture.GrantAsync(world.Workspace, member, Role.Contributor);

        UseCaseResult<PasswordResetIssuedResponse> result = await Reset(world.Workspace, admin, member);

        Assert.True(result.IsSuccess, result.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.ResetToken));

        using Session session = _fixture.OpenUnscopedSession();
        IAccountRecoveryService recovery = session.Resolve<IAccountRecoveryService>();

        Assert.Equal(RecoveryOutcome.Succeeded,
            await recovery.CompleteAsync(result.Value.ResetToken, "a-brand-new-password", TestToken.None));

        // Single use.
        Assert.Equal(RecoveryOutcome.Rejected,
            await recovery.CompleteAsync(result.Value.ResetToken, "another-new-password", TestToken.None));
    }

    [Fact]
    public async Task an_administrator_of_one_workspace_cannot_reset_somebody_who_also_belongs_to_another()
    {
        World mine = await _fixture.CreateWorldAsync();
        World theirs = await _fixture.CreateWorldAsync();
        UserId admin = await Person("admin");
        UserId shared = await Person("shared");
        await _fixture.GrantAsync(mine.Workspace, admin, Role.Administrator);
        await _fixture.GrantAsync(mine.Workspace, shared, Role.Viewer);

        // Even a project-scoped Viewer grant elsewhere is somewhere the password works.
        await _fixture.GrantAsync(theirs.Workspace, shared, Role.Viewer, theirs.AlphaId);

        UseCaseResult<PasswordResetIssuedResponse> refused = await Reset(mine.Workspace, admin, shared);

        Assert.Equal(ExecutionOutcome.Denied, refused.Outcome);
        Assert.Null(refused.Value);
        Assert.Equal(0, await LiveRecoveryTokens(shared));

        // An administrator of both may.
        await _fixture.GrantAsync(theirs.Workspace, admin, Role.Administrator);

        UseCaseResult<PasswordResetIssuedResponse> allowed = await Reset(mine.Workspace, admin, shared);

        Assert.True(allowed.IsSuccess, allowed.Reason);
    }

    [Fact]
    public async Task a_project_scoped_administrator_elsewhere_is_not_enough()
    {
        World mine = await _fixture.CreateWorldAsync();
        World theirs = await _fixture.CreateWorldAsync();
        UserId admin = await Person("admin");
        UserId shared = await Person("shared");
        await _fixture.GrantAsync(mine.Workspace, admin, Role.Administrator);
        await _fixture.GrantAsync(theirs.Workspace, admin, Role.Administrator, theirs.AlphaId);
        await _fixture.GrantAsync(mine.Workspace, shared, Role.Viewer);
        await _fixture.GrantAsync(theirs.Workspace, shared, Role.Viewer);

        Assert.Equal(ExecutionOutcome.Denied, (await Reset(mine.Workspace, admin, shared)).Outcome);
    }

    [Fact]
    public async Task nobody_resets_their_own_account_this_way()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId admin = await Person("admin");
        await _fixture.GrantAsync(world.Workspace, admin, Role.Administrator);

        UseCaseResult<PasswordResetIssuedResponse> result = await Reset(world.Workspace, admin, admin);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await LiveRecoveryTokens(admin));
    }

    [Fact]
    public async Task a_reviewer_cannot_reset_anybody()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await Person("reviewer");
        UserId member = await Person("member");
        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Reviewer);
        await _fixture.GrantAsync(world.Workspace, member, Role.Viewer);

        Assert.Equal(ExecutionOutcome.Denied, (await Reset(world.Workspace, reviewer, member)).Outcome);
        Assert.Equal(0, await LiveRecoveryTokens(member));
    }

    [Fact]
    public async Task somebody_outside_the_workspace_is_not_found_and_a_disabled_account_gets_no_reset()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId admin = await Person("admin");
        UserId stranger = await Person("stranger");
        UserId disabled = await Person("disabled");
        await _fixture.GrantAsync(world.Workspace, admin, Role.Administrator);
        await _fixture.GrantAsync(world.Workspace, disabled, Role.Viewer);
        await _fixture.DisableAccountAsync(disabled);

        Assert.Equal(ExecutionOutcome.NotFound, (await Reset(world.Workspace, admin, stranger)).Outcome);

        UseCaseResult<PasswordResetIssuedResponse> refused = await Reset(world.Workspace, admin, disabled);

        Assert.False(refused.IsSuccess);
        Assert.Equal(0, await LiveRecoveryTokens(disabled));
    }

    [Fact]
    public async Task the_token_is_never_written_to_the_audit_trail()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId admin = await Person("admin");
        UserId member = await Person("member");
        await _fixture.GrantAsync(world.Workspace, admin, Role.Administrator);
        await _fixture.GrantAsync(world.Workspace, member, Role.Viewer);

        UseCaseResult<PasswordResetIssuedResponse> result = await Reset(world.Workspace, admin, member);
        Assert.True(result.IsSuccess, result.Reason);

        using Session session = _fixture.OpenUnscopedSession();
        string pattern = $"%{result.Value!.ResetToken}%";

        int issued = await session.Db.Database
            .SqlQuery<int>($@"
                select count(*)::int as ""Value"" from audit_events
                where action = {(int)Domain.Auditing.AuditAction.PasswordResetIssued} and resource_reference = {"issue_password_reset:" + member}")
            .SingleAsync();
        int leaked = await session.Db.Database
            .SqlQuery<int>($@"
                select count(*)::int as ""Value"" from audit_events
                where resource_reference like {pattern} or details::text like {pattern}")
            .SingleAsync();

        Assert.Equal(1, issued);
        Assert.Equal(0, leaked);
    }

    private Task<UserId> Person(string label) =>
        _fixture.CreateUserAsync($"reset-{label}-{Guid.NewGuid():N}@example.com");

    private async Task<UseCaseResult<PasswordResetIssuedResponse>> Reset(
        WorkspaceId workspace, UserId caller, UserId subject)
    {
        using Session session = _fixture.OpenSession(workspace);
        var useCase = new IssuePasswordResetUseCase(
            session.Resolve<IAccessDirectory>(), session.Resolve<IAccountRecoveryService>());

        return await session.RunAsync(useCase, new IssuePasswordResetRequest(workspace, subject), World.Human(caller));
    }

    private async Task<int> LiveRecoveryTokens(UserId user)
    {
        using Session session = _fixture.OpenUnscopedSession();

        return await session.Db.RecoveryTokens.CountAsync(token => token.UserId == user.Value && token.UsedAt == null);
    }
}
