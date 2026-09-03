using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// Where analysis reads from, and how much of it.
/// <para>
/// DevBuddy never fetches a checkout itself. Cloning means running git, and running anything is
/// precisely what control SB-04 forbids while a repository is under study. An operator mounts the
/// working copy, read-only, and points <see cref="RootPath"/> at it.
/// </para>
/// </summary>
public sealed class AnalysisOptions
{
    public const string SectionName = "Analysis";

    /// <summary>
    /// Base directory holding one subdirectory per project, named by project identifier. A
    /// project with no directory under it is simply not analysable, which is the right answer
    /// rather than an error worth crashing over.
    /// </summary>
    public string RootPath { get; set; } = "./.data/projects";

    /// <summary>
    /// Files larger than this are listed but not read. A repository under study is
    /// attacker-controlled, and an analyser that reads a 2 GB file into memory is a denial of
    /// service anyone who can commit to it can perform (SB-21).
    /// </summary>
    public long MaxFileBytes { get; set; } = 2L * 1024 * 1024;

    /// <summary>Ceiling on files visited per analysis, for the same reason.</summary>
    public int MaxFilesVisited { get; set; } = 20_000;

    /// <summary>How long one analysis may run before it is cancelled (SB-22).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many analyses may run at once, across the whole process.
    /// <para>
    /// The other limits bound one run; this one bounds twenty starting together, which is
    /// trivially reachable by an assistant looping over projects and is the most expensive thing
    /// this system does (SB-21).
    /// </para>
    /// </summary>
    public int MaxConcurrentAnalyses { get; set; } = 4;

    /// <summary>
    /// How long a caller waits for a slot before being told the system is busy. Short, because a
    /// caller waiting indefinitely cannot tell a busy system from a broken one.
    /// </summary>
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Directories never walked. Build output and dependency caches are noise.</summary>
    public IList<string> IgnoredDirectories { get; } =
        ["bin", "obj", "node_modules", ".vs", ".idea", "dist", "target", "__pycache__"];

    /// <summary>The directory a project is analysed from.</summary>
    public string RootFor(ProjectScope scope) =>
        Path.Combine(Path.GetFullPath(RootPath), scope.ProjectId.Value.ToString());

    public string RootFor(ProjectScope scope, SourceRepositoryId? repositoryId) =>
        repositoryId is { } repository
            ? Path.Combine(RootFor(scope), repository.Value.ToString())
            : RootFor(scope);

    /// <summary>True when the directory a scope maps to exists and can be walked.</summary>
    public bool IsAnalysable(ProjectScope scope, SourceRepositoryId? repositoryId = null) =>
        Directory.Exists(RootFor(scope, repositoryId));

    internal PathGuardFactory GuardFor(ProjectScope scope, SourceRepositoryId? repositoryId) =>
        new(RootFor(scope, repositoryId));
}

/// <summary>
/// Builds the path guard for one analysis. A separate type so the root is resolved once per run
/// and every file read in that run is checked against the same resolved root.
/// </summary>
internal sealed record PathGuardFactory(string Root)
{
    public Scanning.PathGuard Create() => new(Guard.NotBlank(Root, nameof(Root)));
}
