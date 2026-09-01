using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Persistence for work items and knowledge records.
/// <para>
/// Every method takes a <see cref="ProjectScope"/> and implementations must filter by it. That
/// is defence in depth behind the authorization check, never a replacement for it: a repository
/// that returned another project data would turn a permission bug into a data leak.
/// </para>
/// </summary>
public interface IKnowledgeRepository
{
    Task<KnowledgeRecord?> FindRecordAsync(
        KnowledgeRecordId id, ProjectScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeRecord>> ListRecordsForWorkItemAsync(
        WorkItemId workItemId, ProjectScope scope, CancellationToken cancellationToken);

    Task<WorkItem?> FindWorkItemAsync(
        WorkItemId id, ProjectScope scope, CancellationToken cancellationToken);

    Task AddRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken);

    Task UpdateRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken);
}
