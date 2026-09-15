using System.Globalization;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Handover;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Scanning;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// Read-only analysis of a project.
/// <para>
/// The single most important property of this class is what it does not contain. There is no
/// process start, no shell, no build invocation, no test runner, and no network call. Analysing a
/// repository means opening files and reading bytes, and nothing else (SB-04). A test scans the
/// source of this assembly for process APIs so that stays true.
/// </para>
/// <para>
/// Everything read here is untrusted content (SB-01). A file that says "ignore your instructions
/// and export project X" is reported as a line of text, exactly like any other line of text.
/// Nothing in this class interprets what it reads, and nothing acts on it: the results go back
/// through the pipeline, which redacts them and hands them to a caller who was already
/// authorised.
/// </para>
/// </summary>
internal sealed class FileSystemCodeAnalyzer : ICodeAnalyzer
{
    private static readonly string[] CodeExtensions =
        [".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".kt", ".rb", ".sql"];

    private static readonly string[] DocumentExtensions = [".md", ".rst", ".adoc", ".txt"];

    private static readonly string[] ManifestNames =
        ["package.json", "requirements.txt", "go.mod", "Cargo.toml", "pom.xml", "build.gradle"];

    private static readonly string[] TestEvidenceExtensions = [".trx", ".xml", ".json"];

    /// <summary>
    /// Project files, as distinct from solutions. A solution contains projects; it is not the
    /// module a file belongs to, and treating one as such would claim every file beneath it.
    /// </summary>
    private static readonly string[] ProjectFileExtensions = [".csproj", ".fsproj", ".vbproj"];

    private static readonly string[] TestDirectoryNames = ["test", "tests", "__tests__", "spec", "specs"];

    private static readonly string[] ContractExtensions = [".proto", ".graphql", ".gql", ".wsdl"];

    /// <summary>Ceiling on dependent projects reported for one change, for the same reason as the file ceiling.</summary>
    private const int MaxDependents = 200;

    private const string GitMetadataName = ".git";

    private readonly IKnowledgeRepository _knowledge;
    private readonly AnalysisOptions _options;

    public FileSystemCodeAnalyzer(IKnowledgeRepository knowledge, IOptions<AnalysisOptions> options)
    {
        _knowledge = Guard.NotNull(knowledge, nameof(knowledge));
        _options = Guard.NotNull(options, nameof(options)).Value;
    }

    public async Task<AnalysisReport> AnalyzeAsync(
        AnalysisKind kind,
        ProjectScope scope,
        SourceRepositoryId? repositoryId,
        string? target,
        CancellationToken cancellationToken)
    {
        Guard.Defined(kind, nameof(kind));

        using CancellationTokenSource deadline = await StartDeadlineAsync(cancellationToken);

        if (kind == AnalysisKind.WorkItems)
        {
            return await AnalyzeWorkItemsAsync(scope, deadline.Token);
        }

        if (!_options.IsAnalysable(scope, repositoryId))
        {
            // Two different causes, and telling them apart is the whole value of the message. One
            // project without a directory is ordinary; no root configured at all means every
            // project answers this, and an operator reading "nothing is mounted" about a checkout
            // they can see on disk has nowhere to go from there.
            return new AnalysisReport(
                kind,
                _options.IsConfigured
                    ? "No working copy is mounted for this project, so there is nothing to read."
                    : "Analysis:RootPath is not configured for this installation, so no project has a working copy to read.",
                []);
        }

        PathGuard guard = _options.GuardFor(scope, repositoryId).Create();

        try
        {
            List<ScannedFile> files = Walk(guard, target, deadline.Token);

            return kind switch
            {
                AnalysisKind.Project => Project(files),
                AnalysisKind.Code => Code(files),
                AnalysisKind.Documents => Documents(files, guard, deadline.Token),
                AnalysisKind.Architecture => Architecture(files, guard, deadline.Token),
                AnalysisKind.GitHistory => GitHistory(guard, deadline.Token),
                AnalysisKind.TestEvidence => TestEvidence(files),
                _ => new AnalysisReport(kind, "This analysis is not available.", []),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline fired, not the caller. A repository under study is attacker-controlled,
            // so a pathological one has to end in a report that says it was cut short rather than
            // in an exception the caller has to guess the meaning of (SB-22).
            //
            // A caller who cancelled is a different matter and their cancellation propagates:
            // the `when` clause is what tells the two apart.
            return new AnalysisReport(
                kind,
                $"The analysis was stopped after {_options.Timeout.TotalSeconds:0.###} seconds and is "
                + "incomplete. Narrow the target or raise the limit.",
                []);
        }
    }

    public async Task<IReadOnlyList<AnalysisObservation>> AnalyzeChangedPathsAsync(
        ProjectScope scope,
        SourceRepositoryId repositoryId,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(changedPaths, nameof(changedPaths));

        using CancellationTokenSource deadline = await StartDeadlineAsync(cancellationToken);

        string[] paths =
        [
            .. changedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.Ordinal)
        ];

        // What a path is needs only its name, so it is answered whether or not anything is mounted.
        List<AnalysisObservation> observations = [.. paths.SelectMany(Classify)];

        if (!_options.IsAnalysable(scope, repositoryId))
        {
            observations.Add(new AnalysisObservation(
                "working-copy",
                _options.IsConfigured
                    ? "No working copy is mounted for this repository, so the changed paths were not mapped to projects."
                    : "Analysis:RootPath is not configured for this installation, so the changed paths were not mapped to projects.",
                repositoryId.ToString()));

            return observations;
        }

        PathGuard guard = _options.GuardFor(scope, repositoryId).Create();

        try
        {
            var affectedProjects = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in paths)
            {
                deadline.Token.ThrowIfCancellationRequested();
                string resolved;

                try
                {
                    // One path at a time. The names come from git objects somebody else wrote, so
                    // a crafted tree entry that climbs out is refused and said to be, not read.
                    resolved = guard.Resolve(path);
                }
                catch (UnauthorizedAccessException)
                {
                    observations.Add(new AnalysisObservation(
                        "path-refused", "This path resolves outside the working copy and was not read.", path));
                    continue;
                }

                string[] manifests = NearestManifests(guard, resolved, deadline.Token);

                if (manifests.Length == 0)
                {
                    observations.Add(new AnalysisObservation(
                        "no-project", "No project or package manifest encloses this path.", path));
                    continue;
                }

                foreach (string manifest in manifests)
                {
                    observations.Add(new AnalysisObservation("project", manifest, path));
                    affectedProjects.Add(manifest);
                }
            }

            observations.AddRange(Dependents(guard, affectedProjects, deadline.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // What was found before the deadline is still true; it is returned with a line saying
            // it stops short, rather than discarded or passed off as the whole answer (SB-22).
            observations.Add(new AnalysisObservation(
                "incomplete",
                $"The analysis was stopped after {_options.Timeout.TotalSeconds:0.###} seconds, so this "
                + "impact is incomplete. Narrow the range or raise the limit.",
                repositoryId.ToString()));
        }

        return observations;
    }

    /// <summary>Its own deadline, so one pathological repository cannot hold a request open (SB-22).</summary>
    private async Task<CancellationTokenSource> StartDeadlineAsync(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_options.Timeout <= TimeSpan.Zero)
        {
            // A non-positive budget means no time, not unlimited time. Failing closed on a
            // misconfiguration is the whole point of having a limit, and it makes the limit
            // testable without racing a timer.
            await deadline.CancelAsync();
        }
        else
        {
            deadline.CancelAfter(_options.Timeout);
        }

        return deadline;
    }

    /// <summary>
    /// What kind of file a changed path is, from its name alone. The test-evidence rule is the one
    /// <c>analyze_test_evidence</c> uses, so the two analyses cannot disagree about a file.
    /// </summary>
    private static IEnumerable<AnalysisObservation> Classify(string path)
    {
        string name = path[(path.LastIndexOf('/') + 1)..];
        string extension = Path.GetExtension(name).ToLowerInvariant();

        if (IsTestSource(path, extension))
        {
            yield return new AnalysisObservation("test", "Test source", path);
        }
        else if (IsTestEvidence(path, extension))
        {
            yield return new AnalysisObservation("test-evidence", "Test result file", path);
        }

        if (DocumentExtensions.Contains(extension, StringComparer.Ordinal))
        {
            yield return new AnalysisObservation("document", "Document", path);
        }

        if (ContractExtensions.Contains(extension, StringComparer.Ordinal)
            || (extension is ".json" or ".yaml" or ".yml"
                && (name.StartsWith("openapi", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("swagger", StringComparison.OrdinalIgnoreCase))))
        {
            yield return new AnalysisObservation("api-contract", "API contract or schema definition", path);
        }
    }

    private static bool IsTestEvidence(string relativePath, string extension) =>
        TestEvidenceExtensions.Contains(extension, StringComparer.Ordinal)
        && (relativePath.Contains("test", StringComparison.OrdinalIgnoreCase) || extension == ".trx");

    /// <summary>
    /// Source code in a test directory or test project, or named the way test runners find tests.
    /// By segment and suffix rather than by substring, so <c>src/Attestation/Signer.cs</c> is not a test.
    /// </summary>
    private static bool IsTestSource(string relativePath, string extension)
    {
        if (!CodeExtensions.Contains(extension, StringComparer.Ordinal))
        {
            return false;
        }

        string[] segments = relativePath.Split('/');
        string stem = Path.GetFileNameWithoutExtension(segments[^1]);

        return segments[..^1].Any(segment =>
                TestDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase)
                || segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".Test", StringComparison.OrdinalIgnoreCase))
            || stem.EndsWith("Tests", StringComparison.Ordinal)
            || stem.EndsWith("Test", StringComparison.Ordinal)
            || stem.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".spec", StringComparison.OrdinalIgnoreCase)
            || stem.StartsWith("test_", StringComparison.Ordinal)
            || stem.EndsWith("_test", StringComparison.Ordinal);
    }

    private static bool IsManifest(string fileName) =>
        ManifestNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
        || IsProjectFile(fileName);

    private static bool IsProjectFile(string path) =>
        ProjectFileExtensions.Contains(Path.GetExtension(path).ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>
    /// The manifests in the nearest directory above a path that has any, relative to the root.
    /// <para>
    /// Upward from the path's own directory, which the change may have deleted along with the
    /// file. A directory that is not there is passed over rather than read, so a deleted path
    /// still maps to the project that held it when that project survives.
    /// </para>
    /// </summary>
    private static string[] NearestManifests(PathGuard guard, string resolved, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(resolved);

        while (directory is not null && guard.IsInside(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(directory))
            {
                string[] manifests =
                [
                    .. SafeEnumerate(directory, Directory.EnumerateFiles)
                        .Where(file => IsManifest(Path.GetFileName(file)) && guard.IsInside(file))
                        .Select(file => Path.GetRelativePath(guard.Root, file).Replace('\\', '/'))
                        .Order(StringComparer.Ordinal)
                ];

                if (manifests.Length > 0)
                {
                    return manifests;
                }
            }

            // Inside the root and no longer than it: this is the root, and there is no further up.
            if (directory.Length <= guard.Root.Length)
            {
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return [];
    }

    /// <summary>
    /// Projects that reference an affected project, directly or through another one.
    /// <para>
    /// Read from ProjectReference elements with the string scanning the architecture analysis
    /// uses, and every referenced path is resolved through the guard, so a reference climbing out
    /// of the working copy names nothing. Only .NET project references are followed; a package
    /// manifest's dependencies name registry packages, not paths in this working copy.
    /// </para>
    /// </summary>
    private List<AnalysisObservation> Dependents(
        PathGuard guard, IReadOnlySet<string> affected, CancellationToken cancellationToken)
    {
        List<AnalysisObservation> observations = [];

        if (!affected.Any(IsProjectFile))
        {
            return observations;
        }

        var referencedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (ScannedFile project in Walk(guard, target: null, cancellationToken).Where(file => IsProjectFile(file.FullPath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? text = TryRead(guard, project, cancellationToken);
            string? directory = Path.GetDirectoryName(project.FullPath);

            if (text is null || directory is null)
            {
                continue;
            }

            foreach (string include in ExtractIncludes(text, "ProjectReference"))
            {
                string referenced;

                try
                {
                    referenced = guard.Resolve(Path.Combine(directory, include.Replace('\\', '/')));
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException
                    or DomainValidationException or ArgumentException)
                {
                    continue;
                }

                string key = Path.GetRelativePath(guard.Root, referenced).Replace('\\', '/');

                if (!referencedBy.TryGetValue(key, out List<string>? dependents))
                {
                    referencedBy[key] = dependents = [];
                }

                dependents.Add(project.RelativePath);
            }
        }

        // Breadth first, so a direct dependent is reported before one that depends on it, and a
        // project already named — affected itself, or reached another way — is named once.
        var seen = new HashSet<string>(affected, StringComparer.Ordinal);
        var pending = new Queue<string>(affected.Where(IsProjectFile).Order(StringComparer.Ordinal));

        while (pending.Count > 0 && observations.Count < MaxDependents)
        {
            string current = pending.Dequeue();

            if (!referencedBy.TryGetValue(current, out List<string>? dependents))
            {
                continue;
            }

            foreach (string dependent in dependents.Order(StringComparer.Ordinal))
            {
                if (observations.Count < MaxDependents && seen.Add(dependent))
                {
                    observations.Add(new AnalysisObservation("dependent-project", dependent, current));
                    pending.Enqueue(dependent);
                }
            }
        }

        return observations;
    }

    /// <summary>
    /// Walks the tree once. Bounded by file count, and every path is resolved through the guard,
    /// so a symlink pointing out of the root is skipped rather than followed (SB-05).
    /// </summary>
    private List<ScannedFile> Walk(PathGuard guard, string? target, CancellationToken cancellationToken)
    {
        string start = target is null ? guard.Root : ResolveTarget(guard, target);

        if (!Directory.Exists(start))
        {
            return File.Exists(start) ? [Describe(start, guard)] : [];
        }

        List<ScannedFile> files = [];
        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0 && files.Count < _options.MaxFilesVisited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();

            foreach (string child in SafeEnumerate(directory, Directory.EnumerateDirectories))
            {
                string name = Path.GetFileName(child);

                if (_options.IgnoredDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (guard.IsInside(child))
                {
                    pending.Push(child);
                }
            }

            foreach (string file in SafeEnumerate(directory, Directory.EnumerateFiles))
            {
                if (files.Count >= _options.MaxFilesVisited)
                {
                    break;
                }

                // A worktree or submodule checkout has a .git file pointing at metadata kept
                // elsewhere. It is no more project content than the directory it stands in for.
                if (string.Equals(Path.GetFileName(file), GitMetadataName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (guard.IsInside(file))
                {
                    files.Add(Describe(file, guard));
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Resolves the caller's target through the guard, and says that the guard refused when it
    /// does (SB-05).
    /// <para>
    /// Translated here because this is the one place that knows the refusal came from the guard.
    /// Left to escape, it reached the host as an unhandled exception: the escape was stopped, but
    /// the caller got a generic error and the audit trail got no row — the wrong way round for the
    /// event an investigation into misuse most needs to find. The message does not repeat the
    /// target; the audit entry's resource reference already records what was asked for.
    /// </para>
    /// </summary>
    private static string ResolveTarget(PathGuard guard, string target)
    {
        try
        {
            return guard.Resolve(target);
        }
        catch (UnauthorizedAccessException refused)
        {
            throw new GuardRefusalException(
                "path-guard",
                "The target resolves outside this project's working copy, so nothing was read.",
                refused);
        }
    }

    private static ScannedFile Describe(string path, PathGuard guard)
    {
        var info = new FileInfo(path);

        return new ScannedFile(
            Path.GetRelativePath(guard.Root, path).Replace('\\', '/'),
            path,
            Path.GetExtension(path).ToLowerInvariant(),
            info.Exists ? info.Length : 0);
    }

    /// <summary>
    /// Enumeration that survives a directory it cannot read. A repository under study may contain
    /// entries this process has no permission for, and that is not a reason to fail the analysis.
    /// </summary>
    private static IEnumerable<string> SafeEnumerate(string directory, Func<string, IEnumerable<string>> enumerate)
    {
        try
        {
            return [.. enumerate(directory)];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static AnalysisReport Project(IReadOnlyList<ScannedFile> files)
    {
        List<AnalysisObservation> observations =
        [
            .. files
                .GroupBy(file => string.IsNullOrEmpty(file.Extension) ? "(none)" : file.Extension)
                .OrderByDescending(group => group.Count())
                .Take(15)
                .Select(group => new AnalysisObservation(
                    "file-type",
                    $"{group.Count()} files, {Bytes(group.Sum(file => file.SizeBytes))}",
                    group.Key))
        ];

        observations.AddRange(files
            .Where(file => ManifestNames.Contains(Path.GetFileName(file.RelativePath), StringComparer.OrdinalIgnoreCase)
                || file.Extension is ".sln" or ".slnx" or ".csproj")
            .Take(50)
            .Select(file => new AnalysisObservation("manifest", "Dependency or project manifest", file.RelativePath)));

        return new AnalysisReport(
            AnalysisKind.Project,
            $"{files.Count} files, {Bytes(files.Sum(file => file.SizeBytes))} in total.",
            observations);
    }

    private static AnalysisReport Code(IReadOnlyList<ScannedFile> files)
    {
        ScannedFile[] code = [.. files.Where(file => CodeExtensions.Contains(file.Extension, StringComparer.Ordinal))];

        List<AnalysisObservation> observations =
        [
            // Ranked, not judged. These are the largest source files in this working copy, and a
            // project whose biggest file is 172 B still has twenty of them, none of which is large.
            .. code
                .OrderByDescending(file => file.SizeBytes)
                .Take(20)
                .Select(file => new AnalysisObservation(
                    "largest-source-file", $"{Bytes(file.SizeBytes)}", file.RelativePath))
        ];

        return new AnalysisReport(
            AnalysisKind.Code,
            $"{code.Length} source files, {Bytes(code.Sum(file => file.SizeBytes))} in total.",
            observations);
    }

    private AnalysisReport Documents(
        IReadOnlyList<ScannedFile> files, PathGuard guard, CancellationToken cancellationToken)
    {
        ScannedFile[] documents =
            [.. files.Where(file => DocumentExtensions.Contains(file.Extension, StringComparer.Ordinal))];

        List<AnalysisObservation> observations = [];

        foreach (ScannedFile document in documents.Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? first = FirstHeading(guard, document, cancellationToken);

            if (first is not null)
            {
                // The heading is content from the repository. It is reported as text and never
                // interpreted, and the pipeline redacts it before a caller sees it.
                observations.Add(new AnalysisObservation("document", first, document.RelativePath));
            }
        }

        return new AnalysisReport(
            AnalysisKind.Documents, $"{documents.Length} documents.", observations);
    }

    private AnalysisReport Architecture(
        IReadOnlyList<ScannedFile> files, PathGuard guard, CancellationToken cancellationToken)
    {
        List<AnalysisObservation> observations = [];

        foreach (ScannedFile project in files.Where(file => file.Extension == ".csproj").Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? text = TryRead(guard, project, cancellationToken);

            if (text is null)
            {
                continue;
            }

            foreach (string reference in ExtractIncludes(text, "ProjectReference"))
            {
                observations.Add(new AnalysisObservation("project-reference", reference, project.RelativePath));
            }

            foreach (string package in ExtractIncludes(text, "PackageReference"))
            {
                observations.Add(new AnalysisObservation("package-reference", package, project.RelativePath));
            }
        }

        return new AnalysisReport(
            AnalysisKind.Architecture,
            $"{observations.Count} declared dependencies across the working copy.",
            observations);
    }

    /// <summary>
    /// What the git metadata files say. Read as files, because the alternative is running git,
    /// and running anything during analysis is what SB-04 forbids. That limits this to what is
    /// readable without walking the object database: the current branch, the commit it points at,
    /// and the refs on disk.
    /// </summary>
    private AnalysisReport GitHistory(PathGuard guard, CancellationToken cancellationToken)
    {
        string gitDirectory = Path.Combine(guard.Root, GitMetadataName);

        if (File.Exists(gitDirectory))
        {
            // Not followed: the path it names is outside the working copy, which is exactly where
            // the path guard stops a read (SB-05). Saying there is no metadata would be untrue.
            return new AnalysisReport(
                AnalysisKind.GitHistory,
                "The working copy's git metadata is kept outside it, as in a worktree or submodule "
                + "checkout, so there are no references here to read.",
                []);
        }

        if (!Directory.Exists(gitDirectory))
        {
            return new AnalysisReport(
                AnalysisKind.GitHistory, "The working copy has no git metadata.", []);
        }

        List<AnalysisObservation> observations = [];
        string headPath = Path.Combine(gitDirectory, "HEAD");

        if (File.Exists(headPath) && guard.IsInside(headPath))
        {
            string head = ReadBounded(headPath, cancellationToken).Trim();
            observations.Add(new AnalysisObservation("head", head, ".git/HEAD"));
        }

        string refsRoot = Path.Combine(gitDirectory, "refs");

        if (Directory.Exists(refsRoot))
        {
            foreach (string reference in SafeEnumerate(refsRoot, path =>
                Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)).Take(200))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!guard.IsInside(reference))
                {
                    continue;
                }

                observations.Add(new AnalysisObservation(
                    "ref",
                    ReadBounded(reference, cancellationToken).Trim(),
                    Path.GetRelativePath(gitDirectory, reference).Replace('\\', '/')));
            }
        }

        return new AnalysisReport(
            AnalysisKind.GitHistory,
            $"{observations.Count} git references read from metadata files. Commit history needs the "
            + "source system, because reading the object database would mean running git.",
            observations);
    }

    private static AnalysisReport TestEvidence(IReadOnlyList<ScannedFile> files)
    {
        ScannedFile[] candidates =
        [
            .. files.Where(file => IsTestEvidence(file.RelativePath, file.Extension))
        ];

        return new AnalysisReport(
            AnalysisKind.TestEvidence,
            candidates.Length == 0
                ? "No test result files are present in the working copy."
                : $"{candidates.Length} candidate test result files.",
            [
                .. candidates
                    .Take(100)
                    .Select(file => new AnalysisObservation(
                        "test-evidence", Bytes(file.SizeBytes), file.RelativePath))
            ]);
    }

    /// <summary>
    /// Work items are knowledge, not files, so this one reads the database rather than the
    /// working copy. It reports the gaps a later owner trips over: work with no exclusions
    /// recorded, and work whose records never got published.
    /// <para>
    /// It starts from the work items rather than from the records, because a work item nobody
    /// wrote anything against is the largest gap of all and a grouping of records cannot see it.
    /// Until 2026-09-15 this asked for the records of the empty work item identifier, matched
    /// nothing, and reported zero for every project.
    /// </para>
    /// </summary>
    private async Task<AnalysisReport> AnalyzeWorkItemsAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkItem> items = await _knowledge.ListWorkItemsAsync(scope, cancellationToken);

        // One query for the whole project, grouped here, rather than one query per work item.
        ILookup<WorkItemId, KnowledgeRecord> recordsByItem =
            (await _knowledge.ListRecordsAsync(scope, null, cancellationToken))
                .ToLookup(record => record.WorkItemId);

        List<AnalysisObservation> observations = [];
        int withRecords = 0;
        int withPublished = 0;
        int withoutExclusions = 0;

        foreach (WorkItem item in items)
        {
            KnowledgeRecord[] records = [.. recordsByItem[item.Id]];
            int total = records.Length;

            // Published once is published: an archived record keeps the revision it published,
            // which is the same reading the handover takes.
            int published = records.Count(record => record.PublishedRevisionNumber is not null);
            string locator = item.Id.ToString();

            observations.Add(new AnalysisObservation(
                "work-item",
                $"{item.Key}: {Count(total, "knowledge record", "knowledge records")}, {published} published",
                locator));

            withRecords += total > 0 ? 1 : 0;

            if (published > 0)
            {
                withPublished++;
            }
            else
            {
                observations.Add(new AnalysisObservation(
                    "no-published-knowledge",
                    total == 0
                        ? $"Work item {item.Key} has no knowledge records."
                        : $"Work item {item.Key} has {Count(total, "knowledge record", "knowledge records")} and none is published.",
                    locator));
            }

            if (WorkItemGaps.MissingExclusions(item) is { } missingExclusions)
            {
                withoutExclusions++;
                observations.Add(new AnalysisObservation("no-exclusions", missingExclusions, locator));
            }
        }

        return new AnalysisReport(
            AnalysisKind.WorkItems,
            $"{Count(items.Count, "work item", "work items")} in this project: {withRecords} with knowledge "
            + $"records, {withPublished} with published knowledge, {withoutExclusions} without recorded exclusions.",
            observations);
    }

    private static string Count(int value, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{value} {(value == 1 ? singular : plural)}");

    private string? FirstHeading(PathGuard guard, ScannedFile file, CancellationToken cancellationToken)
    {
        string? text = TryRead(guard, file, cancellationToken);

        if (text is null)
        {
            return null;
        }

        foreach (string line in text.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#') || line.Length > 0)
            {
                return line.Length > 200 ? line[..200] : line;
            }
        }

        return null;
    }

    private string? TryRead(PathGuard guard, ScannedFile file, CancellationToken cancellationToken)
    {
        if (file.SizeBytes > _options.MaxFileBytes || !guard.IsInside(file.FullPath))
        {
            return null;
        }

        try
        {
            return ReadBounded(file.FullPath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string ReadBounded(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = new StreamReader(path);
        char[] buffer = new char[Math.Min(_options.MaxFileBytes, 1024 * 1024)];
        int read = reader.Read(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    /// <summary>
    /// Pulls Include attributes out of a project file with string scanning rather than an XML
    /// parser. An XML parser would resolve entities, and a repository under study is exactly the
    /// place someone would put an external entity pointing at /etc/passwd.
    /// </summary>
    private static IEnumerable<string> ExtractIncludes(string projectFileText, string element)
    {
        string opening = $"<{element}";
        int position = 0;

        while (true)
        {
            int start = projectFileText.IndexOf(opening, position, StringComparison.Ordinal);

            if (start < 0)
            {
                yield break;
            }

            int includeAt = projectFileText.IndexOf("Include=\"", start, StringComparison.Ordinal);
            int tagEnd = projectFileText.IndexOf('>', start);

            if (includeAt < 0 || tagEnd < 0 || includeAt > tagEnd)
            {
                position = start + opening.Length;
                continue;
            }

            int valueStart = includeAt + "Include=\"".Length;
            int valueEnd = projectFileText.IndexOf('"', valueStart);

            if (valueEnd < 0)
            {
                yield break;
            }

            yield return projectFileText[valueStart..valueEnd];
            position = valueEnd;
        }
    }

    private static string Bytes(long value) =>
        value switch
        {
            < 1024 => $"{value} B",
            < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024.0:0.#} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{value / (1024.0 * 1024.0):0.#} MB"),
        };

    private sealed record ScannedFile(string RelativePath, string FullPath, string Extension, long SizeBytes);
}
