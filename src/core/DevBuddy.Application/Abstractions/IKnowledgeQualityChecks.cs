using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// The knowledge-quality sweeps info.md requires: provenance validation, duplicate detection,
/// and staleness detection. Grouped behind one port because all three read the same corpus and
/// produce the same shape of answer, and splitting them would add ceremony without adding a
/// boundary.
/// </summary>
public interface IKnowledgeQualityChecks
{
    Task<IReadOnlyList<QualityFinding>> ValidateProvenanceAsync(
        ProjectScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualityFinding>> DetectDuplicatesAsync(
        ProjectScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualityFinding>> DetectStalenessAsync(
        ProjectScope scope, DateTimeOffset staleBefore, CancellationToken cancellationToken);
}

/// <summary>
/// One thing worth a human look. Reported rather than corrected: a sweep that silently rewrote
/// records would destroy the traceability the system exists to provide.
/// </summary>
public sealed record QualityFinding(
    KnowledgeRecordId RecordId, string Rule, string Detail);
