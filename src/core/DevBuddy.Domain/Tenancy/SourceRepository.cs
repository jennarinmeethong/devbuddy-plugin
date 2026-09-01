using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// A source-control repository linked to a project. Named SourceRepository rather than
/// Repository so it is never confused with the persistence pattern of the same name.
/// Analysis of one is read-only: see control SB-04.
/// </summary>
public sealed class SourceRepository
{
    public SourceRepository(
        SourceRepositoryId id,
        ProjectScope scope,
        SourceProvider provider,
        string remoteLocator,
        string defaultBranch)
    {
        Id = id;
        Scope = scope;
        Provider = Guard.Defined(provider, nameof(provider));
        RemoteLocator = Guard.NotLongerThan(
            Guard.NotBlank(remoteLocator, nameof(remoteLocator)), 500, nameof(remoteLocator));
        DefaultBranch = Guard.NotLongerThan(
            Guard.NotBlank(defaultBranch, nameof(defaultBranch)), 200, nameof(defaultBranch));
    }

    public SourceRepositoryId Id { get; }

    public ProjectScope Scope { get; }

    public SourceProvider Provider { get; }

    /// <summary>
    /// How the repository is addressed at the provider. Not named as a URL, because it may be
    /// an owner and name pair rather than an absolute address.
    /// </summary>
    public string RemoteLocator { get; }

    public string DefaultBranch { get; private set; }

    public void ChangeDefaultBranch(string defaultBranch) =>
        DefaultBranch = Guard.NotLongerThan(
            Guard.NotBlank(defaultBranch, nameof(defaultBranch)), 200, nameof(defaultBranch));
}
