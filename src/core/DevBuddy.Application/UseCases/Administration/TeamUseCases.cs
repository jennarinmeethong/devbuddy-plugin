using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Administration;

// Team administration. The entity and its table existed since Phase 1 with nothing reading or
// writing them; this is that gap closed. A team groups users inside a workspace and carries no
// permission of its own — Membership still decides what anyone may do, exactly as before.
//
// Every one of these is denied to AI, for the same reason project and account provisioning are:
// info.md permits search, get, analyse, draft, and handover. Administering who is on a team is
// none of those.

public sealed record CreateTeamRequest(WorkspaceId WorkspaceId, string Name)
    : WorkspaceRequest(WorkspaceId), IScannableRequest
{
    public override string ResourceReference => Name;

    public IEnumerable<string> ContentForScanning
    {
        get { yield return Name; }
    }

    public override IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(Name) ? ["A team needs a name."] : [];
}

public sealed record TeamCreatedResponse(TeamId TeamId, string Name) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["teamId"] = TeamId.Value.ToString() };
}

public sealed class CreateTeamUseCase(ITeamDirectory directory)
    : UseCase<CreateTeamRequest, TeamCreatedResponse>
{
    private readonly ITeamDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.CreateTeam;

    protected internal override async Task<TeamCreatedResponse> HandleAsync(
        CreateTeamRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        var team = new Team(TeamId.New(), request.WorkspaceId, request.Name);
        await _directory.AddTeamAsync(team, cancellationToken);

        return new TeamCreatedResponse(team.Id, team.Name);
    }
}

public sealed record RenameTeamRequest(WorkspaceId WorkspaceId, TeamId TeamId, string Name)
    : WorkspaceRequest(WorkspaceId), IScannableRequest
{
    public override string ResourceReference => TeamId.ToString();

    public IEnumerable<string> ContentForScanning
    {
        get { yield return Name; }
    }

    public override IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(Name) ? ["A team needs a name."] : [];
}

public sealed record TeamRenamedResponse(TeamId TeamId, string Name) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["teamId"] = TeamId.Value.ToString() };
}

public sealed class RenameTeamUseCase(ITeamDirectory directory)
    : UseCase<RenameTeamRequest, TeamRenamedResponse>
{
    private readonly ITeamDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.RenameTeam;

    protected internal override async Task<TeamRenamedResponse> HandleAsync(
        RenameTeamRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        Team team = await _directory.FindTeamAsync(request.TeamId, request.WorkspaceId, cancellationToken)
            ?? throw new ResourceNotFoundException($"No team {request.TeamId} in this workspace.");

        team.Rename(request.Name);
        await _directory.UpdateTeamAsync(team, cancellationToken);

        return new TeamRenamedResponse(team.Id, team.Name);
    }
}

public sealed record DeleteTeamRequest(WorkspaceId WorkspaceId, TeamId TeamId) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => TeamId.ToString();
}

public sealed record TeamDeletedResponse(TeamId TeamId) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["teamId"] = TeamId.Value.ToString() };
}

public sealed class DeleteTeamUseCase(ITeamDirectory directory)
    : UseCase<DeleteTeamRequest, TeamDeletedResponse>
{
    private readonly ITeamDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.DeleteTeam;

    protected internal override async Task<TeamDeletedResponse> HandleAsync(
        DeleteTeamRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        Team team = await _directory.FindTeamAsync(request.TeamId, request.WorkspaceId, cancellationToken)
            ?? throw new ResourceNotFoundException($"No team {request.TeamId} in this workspace.");

        await _directory.DeleteTeamAsync(team.Id, request.WorkspaceId, cancellationToken);
        return new TeamDeletedResponse(team.Id);
    }
}

public sealed record ListTeamsRequest(WorkspaceId WorkspaceId) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => "teams";
}

public sealed record TeamSummary(TeamId TeamId, string Name);

public sealed record ListTeamsResponse(IReadOnlyList<TeamSummary> Teams);

public sealed class ListTeamsUseCase(ITeamDirectory directory)
    : UseCase<ListTeamsRequest, ListTeamsResponse>
{
    private readonly ITeamDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListTeams;

    protected internal override async Task<ListTeamsResponse> HandleAsync(
        ListTeamsRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<Team> teams = await _directory.ListTeamsAsync(request.WorkspaceId, cancellationToken);
        return new ListTeamsResponse([.. teams.Select(team => new TeamSummary(team.Id, team.Name))]);
    }
}

public sealed record ListTeamMembersRequest(WorkspaceId WorkspaceId, TeamId TeamId)
    : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => TeamId.ToString();
}

/// <summary>
/// One person in a team. A record rather than a bare identifier, matching how memberships are
/// listed — and because a bare list of identifiers describes itself to a generated client as an
/// array of nothing in particular, which every caller then has to cast.
/// </summary>
public sealed record TeamMemberSummary(UserId UserId);

public sealed record ListTeamMembersResponse(IReadOnlyList<TeamMemberSummary> Members);

public sealed class ListTeamMembersUseCase(ITeamDirectory directory)
    : UseCase<ListTeamMembersRequest, ListTeamMembersResponse>
{
    private readonly ITeamDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListTeamMembers;

    protected internal override async Task<ListTeamMembersResponse> HandleAsync(
        ListTeamMembersRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        await RequireTeamAsync(_directory, request.TeamId, request.WorkspaceId, cancellationToken);
        IReadOnlyList<UserId> members = await _directory.ListTeamMembersAsync(request.TeamId, cancellationToken);
        return new ListTeamMembersResponse([.. members.Select(member => new TeamMemberSummary(member))]);
    }

    internal static async Task<Team> RequireTeamAsync(
        ITeamDirectory directory, TeamId teamId, WorkspaceId workspaceId, CancellationToken cancellationToken) =>
        await directory.FindTeamAsync(teamId, workspaceId, cancellationToken)
            ?? throw new ResourceNotFoundException($"No team {teamId} in this workspace.");
}

public sealed record AddTeamMemberRequest(WorkspaceId WorkspaceId, TeamId TeamId, UserId UserId)
    : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => TeamId.ToString();
}

public sealed record TeamMemberAddedResponse(TeamId TeamId, UserId UserId) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["teamId"] = TeamId.Value.ToString(),
            ["userId"] = UserId.Value.ToString(),
        };
}

public sealed class AddTeamMemberUseCase(ITeamDirectory directory)
    : UseCase<AddTeamMemberRequest, TeamMemberAddedResponse>
{
    private readonly ITeamDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AddTeamMember;

    protected internal override async Task<TeamMemberAddedResponse> HandleAsync(
        AddTeamMemberRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        Team team = await ListTeamMembersUseCase.RequireTeamAsync(
            _directory, request.TeamId, request.WorkspaceId, cancellationToken);

        await _directory.AddTeamMemberAsync(team.Id, request.WorkspaceId, request.UserId, cancellationToken);
        return new TeamMemberAddedResponse(team.Id, request.UserId);
    }
}

public sealed record RemoveTeamMemberRequest(WorkspaceId WorkspaceId, TeamId TeamId, UserId UserId)
    : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => TeamId.ToString();
}

public sealed record TeamMemberRemovedResponse(TeamId TeamId, UserId UserId) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["teamId"] = TeamId.Value.ToString(),
            ["userId"] = UserId.Value.ToString(),
        };
}

public sealed class RemoveTeamMemberUseCase(ITeamDirectory directory)
    : UseCase<RemoveTeamMemberRequest, TeamMemberRemovedResponse>
{
    private readonly ITeamDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.RemoveTeamMember;

    protected internal override async Task<TeamMemberRemovedResponse> HandleAsync(
        RemoveTeamMemberRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        Team team = await ListTeamMembersUseCase.RequireTeamAsync(
            _directory, request.TeamId, request.WorkspaceId, cancellationToken);

        await _directory.RemoveTeamMemberAsync(team.Id, request.UserId, cancellationToken);
        return new TeamMemberRemovedResponse(team.Id, request.UserId);
    }
}
