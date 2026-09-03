using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DevBuddy.McpServer.Tests;

/// <summary>
/// Every port, refusing every call.
/// <para>
/// The tool surface is a question about the catalogue, not about what any port returns, so these
/// tests need a dispatcher without needing a database. Refusing rather than returning empty is
/// deliberate: if listing tools ever started touching a port, these tests would say so instead of
/// quietly passing against fake data.
/// </para>
/// </summary>
internal sealed class NullPorts :
    IKnowledgeRepository,
    ISearchIndex,
    IEvidenceStore,
    ISourceSystemClient,
    ICodeAnalyzer,
    ISecretScanner,
    IRedactor,
    IPersonalDataScanner,
    IPersonalDataRedactor,
    IProjectDirectory,
    ITeamDirectory,
    IWorkspaceProvisioner,
    IEmailSender,
    IAccessDirectory,
    IAuditReader,
    IAuditSink,
    IAdministrativeOperations,
    IKnowledgeQualityChecks,
    IAuthorizationService,
    ICredentialManager,
    IMachineTokenService,
    IClock
{
    public DateTimeOffset UtcNow => throw new NotSupportedException();

    public string Redact(string text) => throw new NotSupportedException();

    public Task<KnowledgeRecord?> FindRecordAsync(
        KnowledgeRecordId id, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<KnowledgeRecord>> ListRecordsForWorkItemAsync(
        WorkItemId workItemId, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<WorkItem?> FindWorkItemAsync(
        WorkItemId id, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<WorkItem>> ListWorkItemsAsync(
        ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<KnowledgeRecord>> ListRecordsAsync(
        ProjectScope scope, IReadOnlyList<RecordStatus>? statuses, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task UpdateRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        KnowledgeSearchCriteria criteria, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> ReindexAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<EvidenceObject?> FindAsync(
        EvidenceObjectId id, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<EvidenceObject>> ListForScopeAsync(
        ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Stream> OpenReadAsync(EvidenceObject evidence, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<EvidenceObject> StoreAsync(
        ProjectScope scope, Stream content, string mediaType, UserId capturedBy,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SourceSnapshot> FetchSnapshotAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ChangeSet> FetchChangeSetAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, string commitOrRange,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PullRequestSummary>> FetchPullRequestsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<IssueSummary>> FetchIssuesAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ReviewThreadSummary>> FetchReviewThreadsAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, int pullRequestNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AnalysisReport> AnalyzeAsync(
        AnalysisKind kind, ProjectScope scope, SourceRepositoryId? repositoryId, string? target,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<PersonalDataScanResult> IPersonalDataScanner.ScanAsync(
        string content, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Project>> ListProjectsForUserAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Project?> FindProjectAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddProjectAsync(Project project, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task DeleteProjectAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Team>> ListTeamsAsync(
        WorkspaceId workspaceId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Team?> FindTeamAsync(
        TeamId id, WorkspaceId workspaceId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddTeamAsync(Team team, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task UpdateTeamAsync(Team team, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task DeleteTeamAsync(TeamId id, WorkspaceId workspaceId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<UserId>> ListTeamMembersAsync(
        TeamId id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddTeamMemberAsync(
        TeamId id, WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RemoveTeamMemberAsync(TeamId id, UserId userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<WorkspaceProvisioningResult> CreateAsync(
        string name, UserId administrator, string? firstProjectName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Membership>> ListMembershipsForWorkspaceAsync(
        WorkspaceId workspaceId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Membership>> ListMembershipsAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddMembershipAsync(Membership membership, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task UpdateMembershipAsync(Membership membership, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Membership?> FindMembershipAsync(MembershipId id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ProjectAiAccessPolicy> GetAiAccessPolicyAsync(
        ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task SaveAiAccessPolicyAsync(
        ProjectAiAccessPolicy policy, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(
        ProjectScope scope, DateTimeOffset occurredFrom, DateTimeOffset occurredUntil,
        UserId? actorId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ExportManifest> ExportAsync(ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BackupManifest> BackupAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RestoreOutcome> RestoreAsync(
        string backupReference, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<QualityFinding>> ValidateProvenanceAsync(
        ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<QualityFinding>> DetectDuplicatesAsync(
        ProjectScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<QualityFinding>> DetectStalenessAsync(
        ProjectScope scope, DateTimeOffset staleBefore, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AccountCreation?> CreateAccountAsync(
        string email, string displayName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<MachineTokenIssued> IssueAsync(
        UserId userId, string name, TimeSpan lifetime, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<UserId?> ResolveAsync(string token, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<MachineTokenSummary>> ListAsync(
        UserId userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> RevokeAsync(
        UserId userId, MachineTokenId id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task SetPasswordAsync(UserId userId, string password, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> HasCredentialAsync(UserId userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>A container holding a dispatcher and nothing that can reach a database.</summary>
    public static ServiceProvider BuildHost()
    {
        var ports = new NullPorts();
        var services = new ServiceCollection();

        services.AddSingleton<IKnowledgeRepository>(ports);
        services.AddSingleton<ISearchIndex>(ports);
        services.AddSingleton<IEvidenceStore>(ports);
        services.AddSingleton<ISourceSystemClient>(ports);
        services.AddSingleton<ICodeAnalyzer>(ports);
        services.AddSingleton<ISecretScanner>(ports);
        services.AddSingleton<IRedactor>(ports);
        services.AddSingleton<IPersonalDataScanner>(ports);
        services.AddSingleton<IPersonalDataRedactor>(ports);
        services.AddSingleton<IProjectDirectory>(ports);
        services.AddSingleton<ITeamDirectory>(ports);
        services.AddSingleton<IWorkspaceProvisioner>(ports);
        services.AddSingleton<IEmailSender>(ports);
        services.AddSingleton<IAccessDirectory>(ports);
        services.AddSingleton<IAuditReader>(ports);
        services.AddSingleton<IAuditSink>(ports);
        services.AddSingleton<IAdministrativeOperations>(ports);
        services.AddSingleton<IKnowledgeQualityChecks>(ports);
        services.AddSingleton<IAuthorizationService>(ports);
        services.AddSingleton<ICredentialManager>(ports);
        services.AddSingleton<IMachineTokenService>(ports);
        services.AddSingleton<IClock>(ports);

        services.AddDevBuddyOperations();
        return services.BuildServiceProvider();
    }
}
