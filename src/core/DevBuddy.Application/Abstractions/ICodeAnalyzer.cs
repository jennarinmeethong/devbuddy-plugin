using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Read-only analysis of a project and its contents.
/// <para>
/// Control SB-04: implementations must never execute builds, restores, tests, or repository
/// scripts. Control SB-01: everything read here is untrusted data, never instructions.
/// Control SB-05: paths must be constrained to the authorised project root.
/// </para>
/// </summary>
public interface ICodeAnalyzer
{
    Task<AnalysisReport> AnalyzeAsync(
        AnalysisKind kind,
        ProjectScope scope,
        SourceRepositoryId? repositoryId,
        string? target,
        CancellationToken cancellationToken);
}

/// <summary>Which read-only analysis to run. One member per analyse tool on the MCP surface.</summary>
public enum AnalysisKind
{
    Project = 1,
    Code = 2,
    Documents = 3,
    Architecture = 4,
    GitHistory = 5,
    WorkItems = 6,
    TestEvidence = 7,
}

/// <summary>
/// The result of one analysis. Free text, so it is treated as untrusted and passed through the
/// redaction stage before it reaches any caller.
/// </summary>
public sealed record AnalysisReport(
    AnalysisKind Kind,
    string Summary,
    IReadOnlyList<AnalysisObservation> Observations);

/// <summary>One thing the analyser noticed, with where it saw it.</summary>
public sealed record AnalysisObservation(string Subject, string Detail, string SourceLocator);
