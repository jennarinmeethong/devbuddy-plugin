using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Analysis;

// Read-only analysis. info.md requires these to exist before a knowledge record is created, so
// that a record is grounded in something the system actually looked at rather than in a summary
// nobody can check.
//
// Everything these read is untrusted data, never instructions (SB-01), and nothing in the
// repository under study is ever executed (SB-04). Both properties belong to the ICodeAnalyzer
// implementation in Phase 6; the use cases here are what make the results auditable and scoped.

public sealed record AnalysisRequest(
    ProjectScope Scope,
    SourceRepositoryId? RepositoryId = null,
    string? Target = null) : ProjectRequest(Scope)
{
    public override string ResourceReference => Target ?? RepositoryId?.ToString() ?? "project";
}

public sealed record AnalysisResponse(AnalysisReport Report) : IRedactableResponse<AnalysisResponse>
{
    public AnalysisResponse Redact(IRedactor redactor) =>
        new(Report with
        {
            Summary = redactor.Redact(Report.Summary),
            Observations =
            [
                .. Report.Observations.Select(observation => observation with
                {
                    Detail = redactor.Redact(observation.Detail),
                    SourceLocator = redactor.Redact(observation.SourceLocator),
                })
            ],
        });
}

/// <summary>
/// Shared shape for the seven read-only analyses. Each concrete use case differs only in which
/// <see cref="AnalysisKind"/> it runs and which catalogue entry it declares, so they are siblings
/// rather than seven copies of the same method.
/// </summary>
public abstract class ReadOnlyAnalysisUseCase(ICodeAnalyzer analyzer, AnalysisKind kind)
    : UseCase<AnalysisRequest, AnalysisResponse>
{
    private readonly ICodeAnalyzer _analyzer = Guard.NotNull(analyzer, nameof(analyzer));

    protected AnalysisKind Kind { get; } = Guard.Defined(kind, nameof(kind));

    protected internal override async Task<AnalysisResponse> HandleAsync(
        AnalysisRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        AnalysisReport report = await _analyzer.AnalyzeAsync(
            Kind, request.Scope, request.RepositoryId, request.Target, cancellationToken);

        return new AnalysisResponse(report);
    }
}

public sealed class AnalyzeProjectUseCase(ICodeAnalyzer analyzer)
    : ReadOnlyAnalysisUseCase(analyzer, AnalysisKind.Project)
{
    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeProject;
}

public sealed class AnalyzeCodeUseCase(ICodeAnalyzer analyzer)
    : ReadOnlyAnalysisUseCase(analyzer, AnalysisKind.Code)
{
    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeCode;
}

public sealed class AnalyzeDocumentsUseCase(ICodeAnalyzer analyzer)
    : ReadOnlyAnalysisUseCase(analyzer, AnalysisKind.Documents)
{
    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeDocuments;
}

public sealed class AnalyzeArchitectureUseCase(ICodeAnalyzer analyzer)
    : ReadOnlyAnalysisUseCase(analyzer, AnalysisKind.Architecture)
{
    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeArchitecture;
}

public sealed class AnalyzeGitHistoryUseCase(ICodeAnalyzer analyzer)
    : ReadOnlyAnalysisUseCase(analyzer, AnalysisKind.GitHistory)
{
    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeGitHistory;
}

public sealed class AnalyzeWorkItemsUseCase(ICodeAnalyzer analyzer)
    : ReadOnlyAnalysisUseCase(analyzer, AnalysisKind.WorkItems)
{
    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeWorkItems;
}

public sealed class AnalyzeTestEvidenceUseCase(ICodeAnalyzer analyzer)
    : ReadOnlyAnalysisUseCase(analyzer, AnalysisKind.TestEvidence)
{
    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeTestEvidence;
}

public sealed record AnalyzeChangeImpactRequest(
    ProjectScope Scope,
    SourceRepositoryId RepositoryId,
    string CommitOrRange) : ProjectRequest(Scope)
{
    public override string ResourceReference => $"{RepositoryId}@{CommitOrRange}";

    public override IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(CommitOrRange)
            ? ["A commit or range is required."]
            : [];
}

public sealed record ChangeImpactResponse(
    string CommitOrRange,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<AnalysisObservation> Impact) : IRedactableResponse<ChangeImpactResponse>
{
    public ChangeImpactResponse Redact(IRedactor redactor) =>
        this with
        {
            Impact =
            [
                .. Impact.Select(observation => observation with
                {
                    Detail = redactor.Redact(observation.Detail),
                    SourceLocator = redactor.Redact(observation.SourceLocator),
                })
            ],
        };
}

/// <summary>
/// Given a commit or diff, identifies the modules, APIs, tests, and documents affected. Required
/// by name in info.md, and the input to a ChangeImpact knowledge record.
/// <para>
/// Three ports: the source client says what changed, the analyser says what that touches in the
/// working copy, and the knowledge repository says which published records cite it. Keeping them
/// separate is what stops change-impact analysis from needing write access to the source system.
/// </para>
/// <para>
/// Every observation's <see cref="AnalysisObservation.SourceLocator"/> names what it is about: a
/// changed path, or for <c>dependent-project</c> the affected project a dependent references.
/// </para>
/// <para>
/// Until 2026-09-15 this passed the changed paths to the analyser joined into one string, which
/// the analyser resolved as a single path that never existed, so every call answered no impact.
/// </para>
/// </summary>
public sealed class AnalyzeChangeImpactUseCase(
    ISourceSystemClient sourceSystem, ICodeAnalyzer analyzer, IKnowledgeRepository knowledge)
    : UseCase<AnalyzeChangeImpactRequest, ChangeImpactResponse>
{
    private readonly ISourceSystemClient _sourceSystem = Guard.NotNull(sourceSystem, nameof(sourceSystem));
    private readonly ICodeAnalyzer _analyzer = Guard.NotNull(analyzer, nameof(analyzer));
    private readonly IKnowledgeRepository _knowledge = Guard.NotNull(knowledge, nameof(knowledge));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeChangeImpact;

    protected internal override async Task<ChangeImpactResponse> HandleAsync(
        AnalyzeChangeImpactRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        ChangeSet changeSet = await _sourceSystem.FetchChangeSetAsync(
            request.RepositoryId, request.Scope, request.CommitOrRange, cancellationToken);

        IReadOnlyList<AnalysisObservation> inWorkingCopy = await _analyzer.AnalyzeChangedPathsAsync(
            request.Scope, request.RepositoryId, changeSet.ChangedPaths, cancellationToken);

        // Every status, because a published record with a newer draft on top is not Published any
        // more and is still live knowledge. Which revision counts is decided below, not here.
        IReadOnlyList<KnowledgeRecord> records =
            await _knowledge.ListRecordsAsync(request.Scope, statuses: null, cancellationToken);

        return new ChangeImpactResponse(
            changeSet.CommitOrRange,
            changeSet.ChangedPaths,
            [.. inWorkingCopy, .. ChangeImpactCitations.Find(records, changeSet.ChangedPaths)]);
    }
}

/// <summary>
/// Which published knowledge cites a changed path.
/// <para>
/// Only the published revision is read (SB-26). A draft citing a path is not knowledge yet, and a
/// newer draft on top of a published record does not change what that record says. An archived
/// record is not current knowledge either. The body is not read: provenance and front matter are
/// the structured places a record says where it came from, and prose that happens to mention a
/// file name is not a citation.
/// </para>
/// <para>
/// The observation carries the record's identifier, kind and revision and never its title or
/// body. This operation is authorised on <c>AnalyzeProject</c>, and a caller who wants the record
/// asks <c>get_record</c>, which is authorised on reading it.
/// </para>
/// </summary>
internal static class ChangeImpactCitations
{
    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ',', ';', '|'];

    private static readonly char[] Enclosing = ['"', '\'', '`', '(', ')', '[', ']', '<', '>'];

    public static IEnumerable<AnalysisObservation> Find(
        IReadOnlyList<KnowledgeRecord> records, IReadOnlyList<string> changedPaths)
    {
        string[] paths =
        [
            .. changedPaths
                .Select(Normalise)
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.Ordinal)
        ];

        foreach (KnowledgeRecord record in records)
        {
            if (record.ArchivedAt is not null || record.PublishedRevision is not { } published)
            {
                continue;
            }

            (string Field, string Value)[] fields =
            [
                ("provenance", published.Provenance.SourceLocator),
                .. published.FrontMatter
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => ($"front matter '{entry.Key}'", entry.Value)),
            ];

            foreach (string path in paths)
            {
                string[] citing = [.. fields.Where(field => Cites(field.Value, path)).Select(field => field.Field)];

                if (citing.Length > 0)
                {
                    yield return new AnalysisObservation(
                        "knowledge-record",
                        $"{record.Kind} record {record.Id}, published revision {published.Number}, "
                        + $"cites this path in its {string.Join(" and ", citing)}.",
                        path);
                }
            }
        }
    }

    /// <summary>
    /// Whether a structured value names this path: exactly, or followed by a ref (<c>@v0.2.0</c>),
    /// a fragment (<c>#L10</c>) or a line (<c>:42</c>). A longer name that merely begins with the
    /// path, such as <c>ImportJob.cs.bak</c>, is a different file and does not match.
    /// </summary>
    internal static bool Cites(string value, string path)
    {
        foreach (string token in value.Trim().Split(Separators, StringSplitOptions.RemoveEmptyEntries).Prepend(value))
        {
            string candidate = Normalise(token);

            if (string.Equals(candidate, path, StringComparison.Ordinal))
            {
                return true;
            }

            if (candidate.Length > path.Length
                && candidate.StartsWith(path, StringComparison.Ordinal)
                && candidate[path.Length] is '@' or '#' or ':')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Repository-relative, forward slashes, no leading <c>./</c> or <c>/</c>. Git paths are case-sensitive.</summary>
    private static string Normalise(string value)
    {
        string path = value.Trim().Trim(Enclosing).Replace('\\', '/');

        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        return path.TrimStart('/');
    }
}
