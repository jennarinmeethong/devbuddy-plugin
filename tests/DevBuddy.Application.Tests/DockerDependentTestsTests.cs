using System.Xml.Linq;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Keeps the Windows CI filter honest.
/// <para>
/// GitHub's Windows runners cannot start Linux containers, so `.github/workflows/ci.yml` runs a
/// filtered suite there. That filter names assemblies, and the list went stale: it excluded
/// DevBuddy.Infrastructure.Tests alone, written when that was the only project with a
/// Testcontainers fixture, while Phase 4 onwards put container-backed tests into
/// DevBuddy.Security.Tests and DevBuddy.Api.Tests as well. The Windows job then failed on every
/// push with two hundred Docker connection errors — which is loud, but only after the fact, and
/// only for whoever reads a red build that has been red for other reasons before.
/// </para>
/// <para>
/// This test is the second place the change has to be made. Adding a Testcontainers dependency to
/// a test project fails here with the exact line to put in the workflow.
/// </para>
/// </summary>
public sealed class DockerDependentTestsTests
{
    /// <summary>
    /// The assemblies `ci.yml` excludes on Windows. Kept in the same order the filter writes them
    /// so the failure message can be pasted straight in.
    /// </summary>
    private static readonly string[] ExcludedOnWindows =
    [
        "DevBuddy.Api.Tests",
        "DevBuddy.Infrastructure.Tests",
        "DevBuddy.Security.Tests",
    ];

    [Fact]
    public void every_test_project_needing_docker_is_excluded_from_the_windows_run()
    {
        string[] needDocker = TestProjects()
            .Where(project => TakesTestcontainers(project.Document))
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            needDocker.SequenceEqual(ExcludedOnWindows, StringComparer.Ordinal),
            "The test projects that need a Docker engine have changed. Update the Windows filter "
            + "in .github/workflows/ci.yml and the list in this test to match:\n  --filter \""
            + string.Join(" & ", needDocker.Select(name => $"FullyQualifiedName!~{name}"))
            + "\"\nFound: " + string.Join(", ", needDocker)
            + "\nExpected: " + string.Join(", ", ExcludedOnWindows));
    }

    [Fact]
    public void the_workflow_filter_matches_that_list()
    {
        // Reading the workflow rather than trusting the constant above. A list that agrees with
        // itself and not with the file it describes would pass the test above and still leave the
        // Windows job failing.
        string workflow = File.ReadAllText(RepositoryLayout.ProjectFile(".github/workflows/ci.yml"));

        foreach (string assembly in ExcludedOnWindows)
        {
            Assert.True(
                workflow.Contains($"FullyQualifiedName!~{assembly}", StringComparison.Ordinal),
                $"ci.yml does not exclude {assembly} from the Windows run, but that project needs "
                + "a Docker engine.");
        }
    }

    private static bool TakesTestcontainers(XDocument project) =>
        project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Any(name => name.StartsWith("Testcontainers", StringComparison.Ordinal));

    private static IEnumerable<(string Name, XDocument Document)> TestProjects() =>
        RepositoryLayout.Root
            .GetDirectories("tests")
            .SelectMany(tests => tests.GetFiles("*.csproj", SearchOption.AllDirectories))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (Path.GetFileNameWithoutExtension(file.Name), XDocument.Load(file.FullName)));
}
