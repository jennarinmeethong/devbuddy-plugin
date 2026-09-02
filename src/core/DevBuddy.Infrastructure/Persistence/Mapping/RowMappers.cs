using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Infrastructure.Persistence.Mapping;

/// <summary>
/// Translation between the domain aggregates and the schema.
/// <para>
/// Explicit, and therefore checkable. Everything that reaches the database goes through a method
/// here, so a question like "where does the approved content hash get written" has one answer.
/// The cost of ADR-0011 lives in this file.
/// </para>
/// </summary>
internal static class RowMappers
{
    // Tenancy and access. These aggregates are rebuilt through their real public API rather than
    // through a rehydration seam, because their transitions take no arguments the database does
    // not already hold.

    public static Workspace ToDomain(WorkspaceRow row) =>
        new(new WorkspaceId(row.Id), row.Name, new UserId(row.CreatedBy), row.CreatedAt);

    public static WorkspaceRow ToRow(Workspace workspace) => new()
    {
        Id = workspace.Id.Value,
        Name = workspace.Name,
        CreatedBy = workspace.CreatedBy.Value,
        CreatedAt = workspace.CreatedAt,
    };

    public static Project ToDomain(ProjectRow row) =>
        new(new ProjectId(row.Id), new WorkspaceId(row.WorkspaceId), row.Name, row.CreatedAt);

    public static ProjectRow ToRow(Project project) => new()
    {
        Id = project.Id.Value,
        WorkspaceId = project.WorkspaceId.Value,
        Name = project.Name,
        CreatedAt = project.CreatedAt,
    };

    public static SourceRepository ToDomain(SourceRepositoryRow row) =>
        new(
            new SourceRepositoryId(row.Id),
            Scope(row),
            (SourceProvider)row.Provider,
            row.RemoteLocator,
            row.DefaultBranch);

    public static SourceRepositoryRow ToRow(SourceRepository repository) => new()
    {
        Id = repository.Id.Value,
        WorkspaceId = repository.Scope.WorkspaceId.Value,
        ProjectId = repository.Scope.ProjectId.Value,
        Provider = (int)repository.Provider,
        RemoteLocator = repository.RemoteLocator,
        DefaultBranch = repository.DefaultBranch,
    };

    public static User ToDomain(UserRow row)
    {
        var user = new User(new UserId(row.Id), row.Email, row.DisplayName, row.CreatedAt);

        if (row.IsDisabled)
        {
            user.Disable();
        }

        return user;
    }

    public static UserRow ToRow(User user) => new()
    {
        Id = user.Id.Value,
        Email = user.Email,
        NormalizedEmail = user.Email.ToUpperInvariant(),
        DisplayName = user.DisplayName,
        CreatedAt = user.CreatedAt,
        IsDisabled = user.IsDisabled,
    };

    public static Membership ToDomain(MembershipRow row)
    {
        Membership membership = row.ProjectId is { } projectId
            ? Membership.ForProject(
                new MembershipId(row.Id),
                new UserId(row.UserId),
                new ProjectScope(new WorkspaceId(row.WorkspaceId), new ProjectId(projectId)),
                (Role)row.Role,
                row.GrantedAt,
                new UserId(row.GrantedBy))
            : Membership.ForWorkspace(
                new MembershipId(row.Id),
                new UserId(row.UserId),
                new WorkspaceId(row.WorkspaceId),
                (Role)row.Role,
                row.GrantedAt,
                new UserId(row.GrantedBy));

        if (row.RevokedAt is { } revokedAt)
        {
            membership.Revoke(revokedAt);
        }

        return membership;
    }

    public static MembershipRow ToRow(Membership membership) => new()
    {
        Id = membership.Id.Value,
        UserId = membership.UserId.Value,
        WorkspaceId = membership.WorkspaceId.Value,
        ProjectId = membership.ProjectId?.Value,
        Role = (int)membership.Role,
        GrantedAt = membership.GrantedAt,
        GrantedBy = membership.GrantedBy.Value,
        RevokedAt = membership.RevokedAt,
    };

    public static ProjectAiAccessPolicy ToDomain(ProjectAiAccessPolicyRow row)
    {
        var policy = new ProjectAiAccessPolicy(Scope(row));

        if (row.IsEnabled && row.EnabledBy is { } enabledBy && row.EnabledAt is { } enabledAt)
        {
            policy.Enable(new UserId(enabledBy), enabledAt, row.BoundedDataScope);
        }

        return policy;
    }

    public static ProjectAiAccessPolicyRow ToRow(ProjectAiAccessPolicy policy) => new()
    {
        WorkspaceId = policy.Scope.WorkspaceId.Value,
        ProjectId = policy.Scope.ProjectId.Value,
        IsEnabled = policy.IsEnabled,
        EnabledBy = policy.EnabledBy?.Value,
        EnabledAt = policy.EnabledAt,
        BoundedDataScope = policy.BoundedDataScope,
    };

    public static WorkItem ToDomain(WorkItemRow row)
    {
        var item = new WorkItem(
            new WorkItemId(row.Id),
            Scope(row),
            row.Key,
            (WorkItemType)row.Type,
            row.Title,
            row.Goal,
            row.CreatedAt,
            new UserId(row.CreatedBy));

        item.SetScope(row.InScope, row.Exclusions);

        foreach (StakeholderJson stakeholder in row.Stakeholders)
        {
            item.AddStakeholder(
                new Stakeholder(stakeholder.Name, (StakeholderRole)stakeholder.Role, stakeholder.Contact));
        }

        foreach (RelatedModuleJson module in row.RelatedModules)
        {
            item.AddRelatedModule(
                new RelatedModule(new SourceRepositoryId(module.RepositoryId), module.ModulePath));
        }

        return item;
    }

    public static WorkItemRow ToRow(WorkItem item) => new()
    {
        Id = item.Id.Value,
        WorkspaceId = item.Scope.WorkspaceId.Value,
        ProjectId = item.Scope.ProjectId.Value,
        Key = item.Key,
        Type = (int)item.Type,
        Title = item.Title,
        Goal = item.Goal,
        InScope = item.InScope,
        Exclusions = item.Exclusions,
        CreatedAt = item.CreatedAt,
        CreatedBy = item.CreatedBy.Value,
        Stakeholders =
        [
            .. item.Stakeholders.Select(stakeholder => new StakeholderJson
            {
                Name = stakeholder.Name,
                Role = (int)stakeholder.Role,
                Contact = stakeholder.Contact,
            })
        ],
        RelatedModules =
        [
            .. item.RelatedModules.Select(module => new RelatedModuleJson
            {
                RepositoryId = module.RepositoryId.Value,
                ModulePath = module.ModulePath,
            })
        ],
    };

    public static KnowledgeRecord ToDomain(KnowledgeRecordRow row) =>
        KnowledgeRecord.Rehydrate(
            new KnowledgeRecordId(row.Id),
            Scope(row),
            new WorkItemId(row.WorkItemId),
            (RecordKind)row.Kind,
            (RecordStatus)row.Status,
            new UserId(row.CreatedBy),
            row.LastUpdatedAt,
            row.ArchivedAt,
            row.PublishedRevisionNumber,
            row.Revisions.Select(ToDomain),
            row.Approvals.OrderBy(approval => approval.Sequence).Select(ToDomain),
            row.Corrections.OrderBy(correction => correction.Sequence).Select(ToDomain));

    public static KnowledgeRecordRow ToRow(KnowledgeRecord record)
    {
        var row = new KnowledgeRecordRow
        {
            Id = record.Id.Value,
            WorkspaceId = record.Scope.WorkspaceId.Value,
            ProjectId = record.Scope.ProjectId.Value,
            WorkItemId = record.WorkItemId.Value,
            Kind = (int)record.Kind,
            Status = (int)record.Status,
            CreatedBy = record.CreatedBy.Value,
            CreatedAt = record.CreatedAt,
            LastUpdatedAt = record.LastUpdatedAt,
            ArchivedAt = record.ArchivedAt,
            PublishedRevisionNumber = record.PublishedRevisionNumber,
        };

        row.Revisions.AddRange(record.Revisions.Select(revision => ToRow(record.Id, revision)));

        // Sequence, not identity: approvals and corrections are an ordered history, and the order
        // is part of what the audit shows.
        row.Approvals.AddRange(
            record.Approvals.Select((approval, index) => ToRow(record.Id, index, approval)));

        row.Corrections.AddRange(
            record.CorrectionRequests.Select((correction, index) => ToRow(record.Id, index, correction)));

        return row;
    }

    public static EvidenceObject ToDomain(EvidenceObjectRow row)
    {
        var evidence = new EvidenceObject(
            new EvidenceObjectId(row.Id),
            Scope(row),
            ContentHash.Parse(row.ContentHash),
            row.MediaType,
            row.SizeBytes,
            row.StorageKey,
            row.CapturedAt,
            new UserId(row.CapturedBy));

        var state = (RedactionState)row.RedactionState;

        if (state != RedactionState.NotScanned && row.ScannedAt is { } scannedAt)
        {
            evidence.RecordScanResult(state, scannedAt);
        }

        return evidence;
    }

    public static EvidenceObjectRow ToRow(EvidenceObject evidence) => new()
    {
        Id = evidence.Id.Value,
        WorkspaceId = evidence.Scope.WorkspaceId.Value,
        ProjectId = evidence.Scope.ProjectId.Value,
        ContentHash = evidence.ContentHash.Value,
        MediaType = evidence.MediaType,
        SizeBytes = evidence.SizeBytes,
        StorageKey = evidence.StorageKey,
        CapturedAt = evidence.CapturedAt,
        CapturedBy = evidence.CapturedBy.Value,
        RedactionState = (int)evidence.RedactionState,
        ScannedAt = evidence.ScannedAt,
    };

    public static AuditEvent ToDomain(AuditEventRow row) =>
        new(
            new AuditEventId(row.Id),
            row.WorkspaceId is { } workspaceId ? new WorkspaceId(workspaceId) : null,
            row.ProjectId is { } projectId ? new ProjectId(projectId) : null,
            new UserId(row.ActorId),
            (AuditAction)row.Action,
            (AuditOutcome)row.Outcome,
            row.ResourceReference,
            row.OccurredAt);

    public static AuditEventRow ToRow(AuditEvent auditEvent) => new()
    {
        Id = auditEvent.Id.Value,
        WorkspaceId = auditEvent.WorkspaceId?.Value,
        ProjectId = auditEvent.ProjectId?.Value,
        ActorId = auditEvent.ActorId.Value,
        Action = (int)auditEvent.Action,
        Outcome = (int)auditEvent.Outcome,
        ResourceReference = auditEvent.ResourceReference,
        OccurredAt = auditEvent.OccurredAt,
    };

    private static RecordRevision ToDomain(RecordRevisionRow row) =>
        new(
            row.Number,
            row.Title,
            row.Body,
            row.FrontMatter,
            ToDomain(row.Provenance),
            row.CreatedAt,
            new UserId(row.CreatedBy));

    private static RecordRevisionRow ToRow(KnowledgeRecordId recordId, RecordRevision revision) => new()
    {
        RecordId = recordId.Value,
        Number = revision.Number,
        Title = revision.Title,
        Body = revision.Body,
        FrontMatter = new Dictionary<string, string>(revision.FrontMatter, StringComparer.Ordinal),
        Provenance = ToRow(revision.Provenance),

        // Written, not recomputed on read. If the stored hash and the recomputed one ever differ,
        // a revision was rewritten in place, and a test can catch that.
        ContentHash = revision.ContentHash.Value,
        CreatedAt = revision.CreatedAt,
        CreatedBy = revision.CreatedBy.Value,
    };

    private static Approval ToDomain(RecordApprovalRow row) =>
        new(
            new UserId(row.ApproverId),
            ContentHash.Parse(row.ApprovedContentHash),
            row.ApprovedRevisionNumber,
            row.ApprovedAt,
            row.ApproverWasDraftCreator);

    private static RecordApprovalRow ToRow(KnowledgeRecordId recordId, int sequence, Approval approval) => new()
    {
        RecordId = recordId.Value,
        Sequence = sequence,
        ApproverId = approval.ApproverId.Value,
        ApprovedContentHash = approval.ApprovedContentHash.Value,
        ApprovedRevisionNumber = approval.ApprovedRevisionNumber,
        ApprovedAt = approval.ApprovedAt,
        ApproverWasDraftCreator = approval.ApproverWasDraftCreator,
    };

    private static CorrectionRequest ToDomain(RecordCorrectionRow row) =>
        new(new UserId(row.RequestedBy), row.TargetRevisionNumber, row.Reason, row.RequestedAt);

    private static RecordCorrectionRow ToRow(
        KnowledgeRecordId recordId, int sequence, CorrectionRequest correction) => new()
        {
            RecordId = recordId.Value,
            Sequence = sequence,
            RequestedBy = correction.RequestedBy.Value,
            TargetRevisionNumber = correction.TargetRevisionNumber,
            Reason = correction.Reason,
            RequestedAt = correction.RequestedAt,
        };

    private static Provenance ToDomain(ProvenanceJson json) =>
        new(
            (ProvenanceSourceKind)json.SourceKind,
            json.SourceLocator,
            json.Author,
            json.RecordedAt,
            json.Evidence.Select(evidence =>
                new EvidenceReference(new EvidenceObjectId(evidence.EvidenceObjectId), evidence.Description)));

    private static ProvenanceJson ToRow(Provenance provenance) => new()
    {
        SourceKind = (int)provenance.SourceKind,
        SourceLocator = provenance.SourceLocator,
        Author = provenance.Author,
        RecordedAt = provenance.RecordedAt,
        Evidence =
        [
            .. provenance.Evidence.Select(evidence => new EvidenceReferenceJson
            {
                EvidenceObjectId = evidence.EvidenceObjectId.Value,
                Description = evidence.Description,
            })
        ],
    };

    private static ProjectScope Scope(ITenantScopedRow row) =>
        new(new WorkspaceId(row.WorkspaceId), new ProjectId(row.ProjectId));
}
