using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Every request states the scope it claims and what it is acting on.
/// <para>
/// Claims, not facts. The pipeline hands these to the authorization service, which re-checks
/// membership server-side. A caller-supplied project identifier is never trusted (SB-11), and
/// making the scope a required part of every request is what guarantees the check has something
/// concrete to verify.
/// </para>
/// </summary>
public interface IUseCaseRequest
{
    WorkspaceId WorkspaceId { get; }

    /// <summary>Null for a workspace-level operation such as listing projects.</summary>
    ProjectId? ProjectId { get; }

    /// <summary>What is being acted on, for the authorization check and the audit entry.</summary>
    string ResourceReference { get; }

    /// <summary>Shape errors, returned before anything else runs. Empty means valid.</summary>
    IReadOnlyList<string> Validate() => [];
}
