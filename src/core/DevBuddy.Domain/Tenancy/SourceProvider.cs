namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// Where a repository is hosted. GitHub is the only provider in the first release; the
/// enum exists so that adding one later does not require a schema change everywhere.
/// </summary>
public enum SourceProvider
{
    GitHub = 1,
}
