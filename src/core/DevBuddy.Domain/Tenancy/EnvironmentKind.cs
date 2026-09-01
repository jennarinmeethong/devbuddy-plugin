namespace DevBuddy.Domain.Tenancy;

/// <summary>
/// What a deployment environment is for. Production is called out because the AI data policy
/// denies production data by default.
/// </summary>
public enum EnvironmentKind
{
    Development = 1,
    Test = 2,
    Staging = 3,
    Production = 4,
}
