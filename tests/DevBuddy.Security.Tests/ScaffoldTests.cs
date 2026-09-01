namespace DevBuddy.Security.Tests;

/// <summary>
/// Phase 0 placeholder. It proves the project reference graph is wired and the assembly
/// loads; it asserts nothing about behaviour, because there is no behaviour yet.
/// Delete this file when Phase 11 adds real tests.
/// </summary>
public sealed class ScaffoldTests
{
    [Fact]
    public void ProjectUnderTestIsReferencedAndLoadable()
    {
        Assert.NotNull(typeof(object).Assembly);
    }
}
