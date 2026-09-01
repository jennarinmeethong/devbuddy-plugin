using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Administration;

// Membership, project AI access, and reading the audit trail. Human-gated, all of them.
//
// Enabling AI access is its own use case, separate from disabling it, so each carries its own
// audit action. One combined operation would have had to pick a single action name and lie about
// half the calls.

public sealed record GrantMembershipRequest(
    WorkspaceId WorkspaceId,
    UserId SubjectUserId,
    Role Role,
    ProjectId? ScopedToProject = null) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => SubjectUserId.ToString();

    public override IReadOnlyList<string> Validate() =>
        SubjectUserId.Value == Guid.Empty ? ["A membership needs a subject user."] : [];
}

public sealed record MembershipResponse(MembershipId MembershipId, Role Role, bool IsActive);

/// <summary>Grants a role, either workspace-wide or on one project.</summary>
public sealed class GrantMembershipUseCase(IAccessDirectory directory, IClock clock)
    : UseCase<GrantMembershipRequest, MembershipResponse>
{
    private readonly IAccessDirectory _directory = Guard.NotNull(directory, nameof(directory));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.GrantMembership;

    protected internal override async Task<MembershipResponse> HandleAsync(
        GrantMembershipRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        Membership membership = request.ScopedToProject is { } projectId
            ? Membership.ForProject(
                MembershipId.New(),
                request.SubjectUserId,
                new ProjectScope(request.WorkspaceId, projectId),
                request.Role,
                _clock.UtcNow,
                caller.UserId)
            : Membership.ForWorkspace(
                MembershipId.New(),
                request.SubjectUserId,
                request.WorkspaceId,
                request.Role,
                _clock.UtcNow,
                caller.UserId);

        await _directory.AddMembershipAsync(membership, cancellationToken);
        return new MembershipResponse(membership.Id, membership.Role, membership.IsActive);
    }
}

public sealed record RevokeMembershipRequest(WorkspaceId WorkspaceId, MembershipId MembershipId)
    : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => MembershipId.ToString();
}

/// <summary>
/// Revokes a grant. It stops subsequent access; it does not recall copies already taken, which
/// is accepted limitation AL-3 and is stated rather than implied.
/// </summary>
public sealed class RevokeMembershipUseCase(IAccessDirectory directory, IClock clock)
    : UseCase<RevokeMembershipRequest, MembershipResponse>
{
    private readonly IAccessDirectory _directory = Guard.NotNull(directory, nameof(directory));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.RevokeMembership;

    protected internal override async Task<MembershipResponse> HandleAsync(
        RevokeMembershipRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        Membership membership =
            await _directory.FindMembershipAsync(request.MembershipId, cancellationToken)
            ?? throw new ResourceNotFoundException($"No membership {request.MembershipId}.");

        if (membership.WorkspaceId != request.WorkspaceId)
        {
            // Reported as not found, not as forbidden. Telling a caller that a membership exists
            // in a workspace they cannot see is itself a disclosure.
            throw new ResourceNotFoundException($"No membership {request.MembershipId}.");
        }

        membership.Revoke(_clock.UtcNow);
        await _directory.UpdateMembershipAsync(membership, cancellationToken);
        return new MembershipResponse(membership.Id, membership.Role, membership.IsActive);
    }
}

public sealed record EnableProjectAiAccessRequest(ProjectScope Scope, string? BoundedDataScope = null)
    : ProjectRequest(Scope)
{
    public override string ResourceReference => "ai_access";
}

public sealed record AiAccessResponse(bool IsEnabled, UserId? EnabledBy, DateTimeOffset? EnabledAt);

/// <summary>
/// Turns on external AI access for one project.
/// <para>
/// The opt-in is per project and is recorded with the person who made it. Customer, production,
/// and personal data stay denied unless a separately approved bounded scope is supplied here, and
/// the prohibition on secrets applies inside any such scope.
/// </para>
/// </summary>
public sealed class EnableProjectAiAccessUseCase(IAccessDirectory directory, IClock clock)
    : UseCase<EnableProjectAiAccessRequest, AiAccessResponse>
{
    private readonly IAccessDirectory _directory = Guard.NotNull(directory, nameof(directory));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.EnableProjectAiAccess;

    protected internal override async Task<AiAccessResponse> HandleAsync(
        EnableProjectAiAccessRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        ProjectAiAccessPolicy policy =
            await _directory.GetAiAccessPolicyAsync(request.Scope, cancellationToken);

        policy.Enable(caller.UserId, _clock.UtcNow, request.BoundedDataScope);
        await _directory.SaveAiAccessPolicyAsync(policy, cancellationToken);

        return new AiAccessResponse(policy.IsEnabled, policy.EnabledBy, policy.EnabledAt);
    }
}

public sealed record DisableProjectAiAccessRequest(ProjectScope Scope) : ProjectRequest(Scope)
{
    public override string ResourceReference => "ai_access";
}

/// <summary>Turns external AI access back off, returning the project to the default of denied.</summary>
public sealed class DisableProjectAiAccessUseCase(IAccessDirectory directory)
    : UseCase<DisableProjectAiAccessRequest, AiAccessResponse>
{
    private readonly IAccessDirectory _directory = Guard.NotNull(directory, nameof(directory));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.DisableProjectAiAccess;

    protected internal override async Task<AiAccessResponse> HandleAsync(
        DisableProjectAiAccessRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        ProjectAiAccessPolicy policy =
            await _directory.GetAiAccessPolicyAsync(request.Scope, cancellationToken);

        policy.Disable();
        await _directory.SaveAiAccessPolicyAsync(policy, cancellationToken);

        return new AiAccessResponse(policy.IsEnabled, policy.EnabledBy, policy.EnabledAt);
    }
}

public sealed record ReadAuditHistoryRequest(
    ProjectScope Scope,
    DateTimeOffset OccurredFrom,
    DateTimeOffset OccurredUntil,
    UserId? ActorId = null) : ProjectRequest(Scope)
{
    public override string ResourceReference => "audit";

    public override IReadOnlyList<string> Validate() =>
        OccurredUntil <= OccurredFrom ? ["The audit window must end after it starts."] : [];
}

public sealed record AuditHistoryResponse(IReadOnlyList<AuditEvent> Entries);

/// <summary>
/// Reads the audit trail for one project. Requires its own permission, separate from reading
/// knowledge: who looked at what is more sensitive than most of what they looked at.
/// </summary>
public sealed class ReadAuditHistoryUseCase(IAuditReader reader)
    : UseCase<ReadAuditHistoryRequest, AuditHistoryResponse>
{
    private readonly IAuditReader _reader = Guard.NotNull(reader, nameof(reader));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ReadAuditHistory;

    protected internal override async Task<AuditHistoryResponse> HandleAsync(
        ReadAuditHistoryRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        IReadOnlyList<AuditEvent> entries = await _reader.QueryAsync(
            request.Scope, request.OccurredFrom, request.OccurredUntil, request.ActorId, cancellationToken);

        return new AuditHistoryResponse(entries);
    }
}
