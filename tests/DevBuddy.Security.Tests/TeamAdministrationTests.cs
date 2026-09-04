using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Closing another of the v1 gaps: the team entity and its table have existed since Phase 1 with
/// nothing reading or writing them. A team carries no permission of its own — administering one
/// is gated by <see cref="PermissionKind.ManageTeams"/>, held only by
/// <see cref="Role.Administrator"/>, the same way project provisioning is.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class TeamAdministrationTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task an_administrator_can_create_rename_staff_and_delete_a_team()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"team-admin-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);

        UserId member = await _fixture.CreateUserAsync($"team-member-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenSession(world.Workspace);
        var directory = session.Resolve<ITeamDirectory>();
        CallerContext caller = World.Human(administrator);

        TeamCreatedResponse created = await SucceedAsync(
            session, new CreateTeamUseCase(directory), new CreateTeamRequest(world.Workspace, "Platform"), caller);

        await SucceedAsync(
            session, new RenameTeamUseCase(directory),
            new RenameTeamRequest(world.Workspace, created.TeamId, "Platform Engineering"), caller);

        ListTeamsResponse listed = await SucceedAsync(
            session, new ListTeamsUseCase(directory), new ListTeamsRequest(world.Workspace), caller);

        Assert.Equal("Platform Engineering", Assert.Single(listed.Teams).Name);

        await SucceedAsync(
            session, new AddTeamMemberUseCase(directory),
            new AddTeamMemberRequest(world.Workspace, created.TeamId, member), caller);

        ListTeamMembersResponse members = await SucceedAsync(
            session, new ListTeamMembersUseCase(directory),
            new ListTeamMembersRequest(world.Workspace, created.TeamId), caller);

        Assert.Equal(member, Assert.Single(members.Members).UserId);

        await SucceedAsync(
            session, new RemoveTeamMemberUseCase(directory),
            new RemoveTeamMemberRequest(world.Workspace, created.TeamId, member), caller);

        ListTeamMembersResponse afterRemoval = await SucceedAsync(
            session, new ListTeamMembersUseCase(directory),
            new ListTeamMembersRequest(world.Workspace, created.TeamId), caller);

        Assert.Empty(afterRemoval.Members);

        await SucceedAsync(
            session, new DeleteTeamUseCase(directory),
            new DeleteTeamRequest(world.Workspace, created.TeamId), caller);

        ListTeamsResponse afterDelete = await SucceedAsync(
            session, new ListTeamsUseCase(directory), new ListTeamsRequest(world.Workspace), caller);

        Assert.Empty(afterDelete.Teams);
    }

    [Fact]
    public async Task a_reviewer_cannot_create_a_team()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId reviewer = await _fixture.CreateUserAsync($"team-reviewer-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, reviewer, Role.Reviewer);

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<TeamCreatedResponse> result = await session.RunAsync(
            new CreateTeamUseCase(session.Resolve<ITeamDirectory>()),
            new CreateTeamRequest(world.Workspace, "Shadow Team"),
            World.Human(reviewer));

        Assert.Equal(ExecutionOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task a_team_in_one_workspace_is_invisible_from_another()
    {
        World alphaWorld = await _fixture.CreateWorldAsync();
        UserId alphaAdmin = await _fixture.CreateUserAsync($"team-iso-a-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(alphaWorld.Workspace, alphaAdmin, Role.Administrator);

        World betaWorld = await _fixture.CreateWorldAsync();
        UserId betaAdmin = await _fixture.CreateUserAsync($"team-iso-b-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(betaWorld.Workspace, betaAdmin, Role.Administrator);

        using Session alphaSession = _fixture.OpenSession(alphaWorld.Workspace);

        await SucceedAsync(
            alphaSession, new CreateTeamUseCase(alphaSession.Resolve<ITeamDirectory>()),
            new CreateTeamRequest(alphaWorld.Workspace, "Alpha Only"),
            World.Human(alphaAdmin));

        using Session betaSession = _fixture.OpenSession(betaWorld.Workspace);

        ListTeamsResponse betaTeams = await SucceedAsync(
            betaSession, new ListTeamsUseCase(betaSession.Resolve<ITeamDirectory>()),
            new ListTeamsRequest(betaWorld.Workspace),
            World.Human(betaAdmin));

        Assert.Empty(betaTeams.Teams);
    }

    private static async Task<TResponse> SucceedAsync<TRequest, TResponse>(
        Session session, UseCase<TRequest, TResponse> useCase, TRequest request,
        CallerContext caller)
        where TRequest : IUseCaseRequest
    {
        UseCaseResult<TResponse> result = await session.RunAsync(useCase, request, caller);

        Assert.True(
            result.IsSuccess,
            $"Expected success but got {result.Outcome}: {result.Reason} "
            + string.Join("; ", result.ValidationErrors));

        return result.Value!;
    }
}
