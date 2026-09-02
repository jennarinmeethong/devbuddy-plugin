using System.Text.RegularExpressions;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Control SB-04, enforced structurally rather than behaviourally.
/// <para>
/// info.md is unambiguous: analysing a repository must not run builds, restores, tests, or
/// repository scripts. A behavioural test can show that one particular hostile fixture did not
/// execute. This one shows that the product code contains no way to execute anything at all,
/// which is the stronger claim and the one that keeps holding as the code grows.
/// </para>
/// <para>
/// A source scan rather than an IL scan, deliberately: it is greppable, it needs no extra
/// dependency, and the thing a reviewer would look for by hand is exactly what it looks for.
/// </para>
/// </summary>
public sealed partial class NoExecutionTests
{
    /// <summary>
    /// APIs that start a process or hand a string to a shell. Written as whole tokens so that a
    /// variable called <c>processed</c> or a comment about a build process does not trip them.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] ForbiddenApis =
    [
        ("Process.Start", ProcessStart()),
        ("ProcessStartInfo", ProcessStartInfo()),
        ("System.Diagnostics.Process", DiagnosticsProcess()),
        ("Assembly.Load from a path", AssemblyLoadFrom()),
        ("Activator.CreateInstanceFrom", ActivatorCreateInstanceFrom()),
    ];

    [Fact]
    public void the_product_code_contains_no_way_to_start_a_process()
    {
        List<string> violations = [];

        foreach (FileInfo file in ProductSourceFiles())
        {
            string source = File.ReadAllText(file.FullName);

            foreach ((string name, Regex pattern) in ForbiddenApis)
            {
                if (pattern.IsMatch(source))
                {
                    violations.Add($"{Relative(file)}: {name}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Analysing a repository must never execute anything (SB-04). Found process or dynamic "
            + "loading APIs in product code: " + string.Join(", ", violations));
    }

    [Fact]
    public void the_scan_actually_looks_at_the_files_it_claims_to()
    {
        FileInfo[] files = [.. ProductSourceFiles()];

        // Without this, a broken path would make the test above pass by scanning nothing. It has
        // happened to better test suites than this one.
        Assert.True(files.Length > 40, $"Expected to scan the product source, found {files.Length} files.");

        Assert.Contains(files, file => file.Name == "FileSystemCodeAnalyzer.cs");
        Assert.Contains(files, file => file.Name == "UseCaseExecutor.cs");
    }

    [Fact]
    public void the_forbidden_pattern_matches_what_it_is_supposed_to()
    {
        // A guard whose pattern silently stopped matching would pass forever. These are the
        // shapes it exists to catch.
        Assert.Matches(ProcessStart(), "var p = Process.Start(\"sh\");");
        Assert.Matches(ProcessStartInfo(), "new ProcessStartInfo(\"dotnet\", \"build\")");
        Assert.Matches(DiagnosticsProcess(), "using System.Diagnostics.Process;");

        // And these are the shapes it must not: ordinary words that contain the same letters.
        Assert.DoesNotMatch(ProcessStart(), "the import process starts with normalisation");
        Assert.DoesNotMatch(ProcessStartInfo(), "processing information about the repository");
    }

    private static IEnumerable<FileInfo> ProductSourceFiles()
    {
        var source = new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "src"));

        return source
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.FullName.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName).Replace('\\', '/');

    [GeneratedRegex(@"\bProcess\s*\.\s*Start\b")]
    private static partial Regex ProcessStart();

    [GeneratedRegex(@"\bProcessStartInfo\b")]
    private static partial Regex ProcessStartInfo();

    [GeneratedRegex(@"\bSystem\.Diagnostics\.Process\b")]
    private static partial Regex DiagnosticsProcess();

    [GeneratedRegex(@"\bAssembly\s*\.\s*LoadFrom\b")]
    private static partial Regex AssemblyLoadFrom();

    [GeneratedRegex(@"\bActivator\s*\.\s*CreateInstanceFrom\b")]
    private static partial Regex ActivatorCreateInstanceFrom();
}
