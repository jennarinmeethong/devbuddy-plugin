using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Work;

/// <summary>
/// A repository, and optionally a module or path within it, that a work item touches.
/// </summary>
public sealed record RelatedModule
{
    public RelatedModule(SourceRepositoryId repositoryId, string? modulePath = null)
    {
        if (repositoryId.Value == Guid.Empty)
        {
            throw new DomainValidationException("A related module requires a repository identifier.");
        }

        RepositoryId = repositoryId;
        ModulePath = modulePath is null
            ? null
            : Guard.NotLongerThan(Guard.NotBlank(modulePath, nameof(modulePath)), 1000, nameof(modulePath));
    }

    public SourceRepositoryId RepositoryId { get; }

    /// <summary>Null means the whole repository.</summary>
    public string? ModulePath { get; }
}
