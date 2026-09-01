using DevBuddy.Application.Pipeline;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases;

/// <summary>
/// Base for a request that names one project. Carrying a <see cref="ProjectScope"/> rather than
/// two loose identifiers means a request cannot be constructed half-scoped.
/// </summary>
public abstract record ProjectRequest(ProjectScope Scope) : IUseCaseRequest
{
    public WorkspaceId WorkspaceId => Scope.WorkspaceId;

    public ProjectId? ProjectId => Scope.ProjectId;

    public abstract string ResourceReference { get; }

    public virtual IReadOnlyList<string> Validate() => [];
}

/// <summary>
/// Base for a request that operates above any single project: listing projects, managing
/// membership, or running an administrative operation.
/// </summary>
public abstract record WorkspaceRequest(WorkspaceId WorkspaceId) : IUseCaseRequest
{
    public ProjectId? ProjectId => null;

    public abstract string ResourceReference { get; }

    public virtual IReadOnlyList<string> Validate() => [];
}
