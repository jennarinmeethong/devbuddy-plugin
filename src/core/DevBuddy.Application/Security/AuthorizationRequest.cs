using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Security;

/// <summary>
/// A question put to the authorization service. The scope comes from the resolved request, and
/// the implementation must re-check membership server-side rather than trusting it (SB-11).
/// </summary>
public sealed record AuthorizationRequest
{
    public AuthorizationRequest(
        CallerContext caller,
        PermissionKind permission,
        WorkspaceId workspaceId,
        ProjectId? projectId,
        string resourceReference)
    {
        Caller = Guard.NotNull(caller, nameof(caller));
        Permission = Guard.Defined(permission, nameof(permission));
        WorkspaceId = workspaceId;
        ProjectId = projectId;
        ResourceReference = Guard.NotLongerThan(
            Guard.NotBlank(resourceReference, nameof(resourceReference)), 500, nameof(resourceReference));
    }

    public CallerContext Caller { get; }

    public PermissionKind Permission { get; }

    public WorkspaceId WorkspaceId { get; }

    /// <summary>Null for a workspace-level operation such as listing projects.</summary>
    public ProjectId? ProjectId { get; }

    /// <summary>What is being acted on. Recorded in the audit entry; never the content itself.</summary>
    public string ResourceReference { get; }
}
