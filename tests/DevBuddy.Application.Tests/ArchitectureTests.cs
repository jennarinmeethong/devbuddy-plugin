using System.Reflection;
using System.Xml.Linq;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The Clean Architecture dependency rule, enforced rather than trusted (ADR-0001).
/// <para>
/// These tests exist because the security model depends on the layering: cross-cutting
/// authorization, redaction, and audit live in one Application pipeline, and that only holds
/// while no host can reach past it. A comment cannot enforce that; a failing build can.
/// </para>
/// </summary>
public sealed class ArchitectureTests
{
    private const string DomainProject = "src/core/DevBuddy.Domain/DevBuddy.Domain.csproj";
    private const string ApplicationProject = "src/core/DevBuddy.Application/DevBuddy.Application.csproj";
    private const string InfrastructureProject = "src/core/DevBuddy.Infrastructure/DevBuddy.Infrastructure.csproj";

    [Fact]
    public void the_domain_project_has_no_project_or_package_references()
    {
        XDocument project = XDocument.Load(RepositoryLayout.ProjectFile(DomainProject));

        string[] projectReferences = ReferencesOf(project, "ProjectReference");
        string[] packageReferences = IncludeAttributesOf(project, "PackageReference");

        Assert.True(
            projectReferences.Length == 0,
            "DevBuddy.Domain must not reference other projects, but references: "
            + string.Join(", ", projectReferences));

        Assert.True(
            packageReferences.Length == 0,
            "DevBuddy.Domain must not take a package dependency, but references: "
            + string.Join(", ", packageReferences));
    }

    [Fact]
    public void the_domain_assembly_binds_only_to_the_framework()
    {
        string[] external = RepositoryLayout.Load("DevBuddy.Domain")
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !IsFrameworkAssembly(name))
            .ToArray();

        Assert.True(
            external.Length == 0,
            "DevBuddy.Domain must depend on nothing but the framework, but binds to: "
            + string.Join(", ", external));
    }

    [Fact]
    public void the_application_layer_does_not_depend_on_infrastructure_or_any_host()
    {
        XDocument project = XDocument.Load(RepositoryLayout.ProjectFile(ApplicationProject));
        string[] references = ReferencesOf(project, "ProjectReference");

        Assert.Equal(["DevBuddy.Domain"], references);
    }

    [Fact]
    public void infrastructure_depends_on_application_and_not_on_a_host()
    {
        XDocument project = XDocument.Load(RepositoryLayout.ProjectFile(InfrastructureProject));
        string[] references = ReferencesOf(project, "ProjectReference");

        Assert.Equal(["DevBuddy.Application"], references);
    }

    [Theory]
    [InlineData("src/hosts/DevBuddy.Api/DevBuddy.Api.csproj")]
    [InlineData("src/hosts/DevBuddy.McpServer/DevBuddy.McpServer.csproj")]
    [InlineData("src/hosts/DevBuddy.Cli/DevBuddy.Cli.csproj")]
    public void a_host_reaches_the_domain_only_through_the_application_layer(string hostProject)
    {
        XDocument project = XDocument.Load(RepositoryLayout.ProjectFile(hostProject));
        string[] references = ReferencesOf(project, "ProjectReference");

        Assert.DoesNotContain("DevBuddy.Domain", references);
        Assert.Contains("DevBuddy.Application", references);
    }

    /// <summary>Project references, reduced to the referenced project name.</summary>
    private static string[] ReferencesOf(XDocument project, string elementName) =>
        IncludeAttributesOf(project, elementName)
            .Select(value => Path.GetFileNameWithoutExtension(value.Replace('\\', '/')))
            .Where(name => !string.IsNullOrEmpty(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Raw Include values. Used for packages, whose names contain dots that are not extensions:
    /// treating coverlet.collector as a file path would report it as coverlet.
    /// </summary>
    private static string[] IncludeAttributesOf(XDocument project, string elementName) =>
        project.Descendants(elementName)
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(value => !string.IsNullOrEmpty(value))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System", StringComparison.Ordinal)
        || name.Equals("netstandard", StringComparison.Ordinal)
        || name.Equals("mscorlib", StringComparison.Ordinal);
}
