using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Fixed values shared by the application tests. Nothing random, so a failure reproduces.
/// </summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public static readonly WorkspaceId Workspace = new(new Guid("11111111-1111-1111-1111-111111111111"));

    public static readonly ProjectId ProjectAlpha = new(new Guid("22222222-2222-2222-2222-222222222222"));

    public static readonly WorkItemId WorkItem = new(new Guid("44444444-4444-4444-4444-444444444444"));

    public static readonly UserId Author = new(new Guid("55555555-5555-5555-5555-555555555555"));

    public static readonly UserId Reviewer = new(new Guid("66666666-6666-6666-6666-666666666666"));

    public static readonly SourceRepositoryId Repository =
        new(new Guid("77777777-7777-7777-7777-777777777777"));

    public static ProjectScope Scope => new(Workspace, ProjectAlpha);

    public static CallerContext Human => new(Author, AccessChannel.Human, "req-human");

    public static CallerContext Ai => new(Author, AccessChannel.Ai, "req-ai");

    public static CallerContext Anonymous => new(default, AccessChannel.Human, "req-anonymous");

    public static Provenance Provenance =>
        new(ProvenanceSourceKind.HumanAuthored, "handover/2026-09-01", "Jennarin", Now);

    public static WorkItem NewWorkItem() =>
        new(WorkItem, Scope, "DEV-101", WorkItemType.Develop, "Import normalisation",
            "Identifiers are normalised before validation runs.", Now, Author);

    public static KnowledgeRecord NewDraft(string body = "The importer normalises first.") =>
        KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(), Scope, WorkItem, RecordKind.Decision,
            "Why the import runs first", body, null, Provenance, Now, Author);
}

/// <summary>
/// Every port, faked in one object, with a single interaction counter.
/// <para>
/// One counter is the point. The authorization enforcement test asserts that a denied execution
/// touched nothing at all, and a single number makes that assertion total: it cannot pass because
/// the test happened to check the wrong port.
/// </para>
/// </summary>
internal sealed class FakePorts :
    IKnowledgeRepository,
    ISearchIndex,
    IEvidenceStore,
    ISourceSystemClient,
    ICodeAnalyzer,
    ISecretScanner,
    IRedactor,
    IProjectDirectory,
    IAccessDirectory,
    IAuditReader,
    IAdministrativeOperations,
    IKnowledgeQualityChecks,
    ICredentialManager,
    IClock
{
    private int _interactions;

    /// <summary>How many times any port method was called.</summary>
    public int Interactions => _interactions;

    public DateTimeOffset UtcNow => TestData.Now;

    public KnowledgeRecord? Record { get; set; }

    public WorkItem? WorkItem { get; set; } = TestData.NewWorkItem();

    public List<KnowledgeRecord> RecordsForWorkItem { get; } = [];

    public List<EvidenceObject> Evidence { get; } = [];

    public Membership? Membership { get; set; }

    public ProjectAiAccessPolicy Policy { get; set; } = new(TestData.Scope);

    public KnowledgeRecord? Saved { get; private set; }

    public WorkItem? SavedWorkItem { get; private set; }

    public Project? SavedProject { get; private set; }

    public string? CreatedAccountEmail { get; private set; }

    /// <summary>Makes account creation return null, the way a duplicate address does.</summary>
    public bool AccountAlreadyExists { get; set; }

    /// <summary>Marks the redactor as having been reached, and makes that visible in output.</summary>
    public string Redact(string text)
    {
        Touch();
        return text.Replace("SECRET", "[REDACTED]", StringComparison.Ordinal);
    }

    public Task<KnowledgeRecord?> FindRecordAsync(
        KnowledgeRecordId id, ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(Record);
    }

    public Task<IReadOnlyList<KnowledgeRecord>> ListRecordsForWorkItemAsync(
        WorkItemId workItemId, ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<KnowledgeRecord>>(RecordsForWorkItem);
    }

    public Task<WorkItem?> FindWorkItemAsync(
        WorkItemId id, ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(WorkItem);
    }

    public Task<IReadOnlyList<WorkItem>> ListWorkItemsAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<WorkItem>>(WorkItem is null ? [] : [WorkItem]);
    }

    public Task<IReadOnlyList<KnowledgeRecord>> ListRecordsAsync(
        ProjectScope scope, IReadOnlyList<RecordStatus>? statuses, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<KnowledgeRecord>>(Record is null ? [] : [Record]);
    }

    public Task AddWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        Touch();
        SavedWorkItem = workItem;
        return Task.CompletedTask;
    }

    public Task AddRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken)
    {
        Touch();
        Saved = record;
        return Task.CompletedTask;
    }

    public Task UpdateRecordAsync(KnowledgeRecord record, CancellationToken cancellationToken)
    {
        Touch();
        Saved = record;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(
        KnowledgeSearchCriteria criteria, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<KnowledgeSearchHit>>(
        [
            new(KnowledgeRecordId.New(), RecordKind.Decision, RecordStatus.Published,
                "Title with SECRET", "Snippet with SECRET", 1.0),
        ]);
    }

    public Task<int> ReindexAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(7);
    }

    public Task<EvidenceObject?> FindAsync(
        EvidenceObjectId id, ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(Evidence.FirstOrDefault());
    }

    public Task<IReadOnlyList<EvidenceObject>> ListForScopeAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<EvidenceObject>>(Evidence);
    }

    public Task<Stream> OpenReadAsync(EvidenceObject evidence, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task<EvidenceObject> StoreAsync(
        ProjectScope scope, Stream content, string mediaType, UserId capturedBy, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new EvidenceObject(
            EvidenceObjectId.New(), scope, ContentHash.FromContent("x"), mediaType, 1, "key",
            TestData.Now, capturedBy));
    }

    public Task<SourceSnapshot> FetchSnapshotAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new SourceSnapshot(repositoryId, "main", "abc123", TestData.Now, ["link"]));
    }

    public Task<ChangeSet> FetchChangeSetAsync(
        SourceRepositoryId repositoryId, ProjectScope scope, string commitOrRange, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new ChangeSet(commitOrRange, ["src/importer.cs"], "Jennarin", TestData.Now));
    }

    public Task<IReadOnlyList<SnapshotDifference>> CompareAsync(
        SourceSnapshot earlier, SourceSnapshot later, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<SnapshotDifference>>(
            [new("branch", earlier.Reference, later.Reference)]);
    }

    public Task<AnalysisReport> AnalyzeAsync(
        AnalysisKind kind, ProjectScope scope, SourceRepositoryId? repositoryId, string? target,
        CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new AnalysisReport(
            kind,
            "Summary mentioning SECRET",
            [new AnalysisObservation("importer", "Detail mentioning SECRET", "src/importer.cs")]));
    }

    public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(content.Contains("SECRET", StringComparison.Ordinal)
            ? new SecretScanResult([new SecretFinding("literal", 1, 6)])
            : SecretScanResult.Clean);
    }

    public Task<IReadOnlyList<Project>> ListProjectsForUserAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<Project>>(
            [new Project(TestData.ProjectAlpha, workspaceId, "Alpha", TestData.Now)]);
    }

    public Task<AccountCreation?> CreateAccountAsync(
        string email, string displayName, CancellationToken cancellationToken)
    {
        Touch();
        CreatedAccountEmail = email;

        return Task.FromResult<AccountCreation?>(
            AccountAlreadyExists
                ? null
                : new AccountCreation(UserId.New(), "setup-token", TestData.Now.AddMinutes(30)));
    }

    public Task SetPasswordAsync(UserId userId, string password, CancellationToken cancellationToken)
    {
        Touch();
        return Task.CompletedTask;
    }

    public Task<bool> HasCredentialAsync(UserId userId, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(true);
    }

    public Task AddProjectAsync(Project project, CancellationToken cancellationToken)
    {
        Touch();
        SavedProject = project;
        return Task.CompletedTask;
    }

    public Task<Project?> FindProjectAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<Project?>(
            new Project(scope.ProjectId, scope.WorkspaceId, "Alpha", TestData.Now));
    }

    public Task<IReadOnlyList<Membership>> ListMembershipsAsync(
        WorkspaceId workspaceId, UserId userId, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<Membership>>(Membership is null ? [] : [Membership]);
    }

    public Task<IReadOnlyList<Membership>> ListMembershipsForWorkspaceAsync(
        WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<Membership>>(Membership is null ? [] : [Membership]);
    }

    public Task AddMembershipAsync(Membership membership, CancellationToken cancellationToken)
    {
        Touch();
        Membership = membership;
        return Task.CompletedTask;
    }

    public Task UpdateMembershipAsync(Membership membership, CancellationToken cancellationToken)
    {
        Touch();
        Membership = membership;
        return Task.CompletedTask;
    }

    public Task<Membership?> FindMembershipAsync(MembershipId id, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(Membership);
    }

    public Task<ProjectAiAccessPolicy> GetAiAccessPolicyAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(Policy);
    }

    public Task SaveAiAccessPolicyAsync(ProjectAiAccessPolicy policy, CancellationToken cancellationToken)
    {
        Touch();
        Policy = policy;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(
        ProjectScope scope, DateTimeOffset occurredFrom, DateTimeOffset occurredUntil, UserId? actorId,
        CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    public Task<ExportManifest> ExportAsync(ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new ExportManifest(
            "export-1", 3, 1, TestData.Now, TestData.Now.AddDays(30)));
    }

    public Task<BackupManifest> BackupAsync(CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new BackupManifest("backup-1", 1024, TestData.Now));
    }

    public Task<RestoreOutcome> RestoreAsync(string backupReference, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new RestoreOutcome(backupReference, true, "Restored."));
    }

    public Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(new HealthReport(true, [new ComponentHealth("database", true, "ok")]));
    }

    public Task<IReadOnlyList<QualityFinding>> ValidateProvenanceAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<QualityFinding>>([]);
    }

    public Task<IReadOnlyList<QualityFinding>> DetectDuplicatesAsync(
        ProjectScope scope, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<QualityFinding>>([]);
    }

    public Task<IReadOnlyList<QualityFinding>> DetectStalenessAsync(
        ProjectScope scope, DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult<IReadOnlyList<QualityFinding>>([]);
    }

    private void Touch() => Interlocked.Increment(ref _interactions);
}

/// <summary>An authorization service whose answer the test decides.</summary>
internal sealed class FakeAuthorizationService : IAuthorizationService
{
    public bool Allow { get; set; } = true;

    public List<AuthorizationRequest> Requests { get; } = [];

    public Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return Task.FromResult(Allow
            ? AuthorizationDecision.Allow()
            : AuthorizationDecision.Deny("The caller is not a member of this project."));
    }
}

/// <summary>Captures what the pipeline wrote to the audit trail.</summary>
internal sealed class FakeAuditSink : IAuditSink
{
    public List<AuditEvent> Entries { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Entries.Add(auditEvent);
        return Task.CompletedTask;
    }
}
