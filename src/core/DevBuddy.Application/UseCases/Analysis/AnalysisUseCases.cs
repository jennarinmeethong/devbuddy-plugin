using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
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
/// Two ports rather than one: the source client says what changed, the analyser says what that
/// means. Keeping them separate is what stops change-impact analysis from needing write access
/// to the source system.
/// </para>
/// </summary>
public sealed class AnalyzeChangeImpactUseCase(ISourceSystemClient sourceSystem, ICodeAnalyzer analyzer)
    : UseCase<AnalyzeChangeImpactRequest, ChangeImpactResponse>
{
    private readonly ISourceSystemClient _sourceSystem = Guard.NotNull(sourceSystem, nameof(sourceSystem));
    private readonly ICodeAnalyzer _analyzer = Guard.NotNull(analyzer, nameof(analyzer));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.AnalyzeChangeImpact;

    protected internal override async Task<ChangeImpactResponse> HandleAsync(
        AnalyzeChangeImpactRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        ChangeSet changeSet = await _sourceSystem.FetchChangeSetAsync(
            request.RepositoryId, request.Scope, request.CommitOrRange, cancellationToken);

        AnalysisReport report = await _analyzer.AnalyzeAsync(
            AnalysisKind.Code,
            request.Scope,
            request.RepositoryId,
            string.Join(";", changeSet.ChangedPaths),
            cancellationToken);

        return new ChangeImpactResponse(changeSet.CommitOrRange, changeSet.ChangedPaths, report.Observations);
    }
}
