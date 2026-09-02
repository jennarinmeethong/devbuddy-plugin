using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DevBuddy.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL, the source of truth for every structured record and relationship (ADR-0003).
/// <para>
/// Two properties of this schema are load-bearing rather than stylistic. Composite indexes lead
/// with workspace and project, so tenant isolation is also the fast path and nobody is ever
/// tempted to drop the scope from a query for performance. And every tenant-scoped table carries
/// a global query filter that fails closed: with no workspace set, it matches nothing.
/// </para>
/// </summary>
public class DevBuddyDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly ITenantContext _tenant;

    public DevBuddyDbContext(DbContextOptions<DevBuddyDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>
    /// The workspace every filtered query is restricted to. <see cref="Guid.Empty"/> when none is
    /// set, which matches no row: unscoped means nothing, not everything.
    /// </summary>
    internal Guid CurrentWorkspace => _tenant.WorkspaceId?.Value ?? Guid.Empty;

    internal DbSet<WorkspaceRow> Workspaces => Set<WorkspaceRow>();

    internal DbSet<TeamRow> Teams => Set<TeamRow>();

    internal DbSet<ProjectRow> Projects => Set<ProjectRow>();

    internal DbSet<SourceRepositoryRow> SourceRepositories => Set<SourceRepositoryRow>();

    internal DbSet<DeploymentEnvironmentRow> DeploymentEnvironments => Set<DeploymentEnvironmentRow>();

    internal DbSet<UserRow> Users => Set<UserRow>();

    internal DbSet<MembershipRow> Memberships => Set<MembershipRow>();

    internal DbSet<ProjectAiAccessPolicyRow> AiAccessPolicies => Set<ProjectAiAccessPolicyRow>();

    internal DbSet<WorkItemRow> WorkItems => Set<WorkItemRow>();

    internal DbSet<KnowledgeRecordRow> KnowledgeRecords => Set<KnowledgeRecordRow>();

    internal DbSet<RecordRevisionRow> RecordRevisions => Set<RecordRevisionRow>();

    internal DbSet<EvidenceObjectRow> EvidenceObjects => Set<EvidenceObjectRow>();

    internal DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    internal DbSet<SourceSnapshotRow> SourceSnapshots => Set<SourceSnapshotRow>();

    internal DbSet<UserCredentialRow> UserCredentials => Set<UserCredentialRow>();

    internal DbSet<RefreshTokenRow> RefreshTokens => Set<RefreshTokenRow>();

    internal DbSet<RecoveryTokenRow> RecoveryTokens => Set<RecoveryTokenRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureTenancy(modelBuilder);
        ConfigureAccess(modelBuilder);
        ConfigureWork(modelBuilder);
        ConfigureKnowledge(modelBuilder);
        ConfigureEvidenceAndAudit(modelBuilder);
        ApplySnakeCaseNames(modelBuilder);
    }

    private void ConfigureTenancy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceRow>(row =>
        {
            row.ToTable("workspaces");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<TeamRow>(row =>
        {
            row.ToTable("teams");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
            row.HasIndex(entity => new { entity.WorkspaceId, entity.Name });
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

        modelBuilder.Entity<ProjectRow>(row =>
        {
            row.ToTable("projects");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
            row.HasIndex(entity => new { entity.WorkspaceId, entity.Id }).IsUnique();
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

        modelBuilder.Entity<SourceRepositoryRow>(row =>
        {
            row.ToTable("source_repositories");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.RemoteLocator).HasMaxLength(500).IsRequired();
            row.Property(entity => entity.DefaultBranch).HasMaxLength(200).IsRequired();
            ScopedIndex(row);
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

        modelBuilder.Entity<DeploymentEnvironmentRow>(row =>
        {
            row.ToTable("deployment_environments");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
            ScopedIndex(row);
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

        modelBuilder.Entity<SourceSnapshotRow>(row =>
        {
            row.ToTable("source_snapshots");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Reference).HasMaxLength(500).IsRequired();
            row.Property(entity => entity.CommitId).HasMaxLength(100).IsRequired();
            JsonColumn(row.Property(entity => entity.Links));
            row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId, entity.RepositoryId });
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });
    }

    private void ConfigureAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRow>(row =>
        {
            row.ToTable("users");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Email).HasMaxLength(320).IsRequired();
            row.Property(entity => entity.NormalizedEmail).HasMaxLength(320).IsRequired();
            row.Property(entity => entity.DisplayName).HasMaxLength(200).IsRequired();

            // Uniqueness is on the normalised form, so two accounts cannot differ only by case.
            row.HasIndex(entity => entity.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<MembershipRow>(row =>
        {
            row.ToTable("memberships");
            row.HasKey(entity => entity.Id);
            row.HasIndex(entity => new { entity.WorkspaceId, entity.UserId, entity.ProjectId });

            // Deliberately not filtered. Revoking access and auditing who had it are workspace
            // administration, and an administrator has to be able to see a grant in order to
            // revoke it. The use cases check the workspace explicitly instead.
        });

        modelBuilder.Entity<ProjectAiAccessPolicyRow>(row =>
        {
            row.ToTable("project_ai_access_policies");
            row.HasKey(entity => new { entity.WorkspaceId, entity.ProjectId });
            row.Property(entity => entity.BoundedDataScope).HasMaxLength(2000);
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

        ConfigureIdentity(modelBuilder);
    }

    /// <summary>
    /// Credentials and tokens. None of these tables is workspace-scoped and none carries a query
    /// filter: an account exists before it belongs to any workspace, and sign-in happens before
    /// there is a tenant context to filter by.
    /// </summary>
    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserCredentialRow>(row =>
        {
            row.ToTable("user_credentials");
            row.HasKey(entity => entity.UserId);
            row.Property(entity => entity.PasswordHash).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<RefreshTokenRow>(row =>
        {
            row.ToTable("refresh_tokens");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.TokenHash).HasMaxLength(64).IsRequired().IsFixedLength();

            // Lookup is by hash, so the index is on the hash. The token itself is never stored.
            row.HasIndex(entity => entity.TokenHash).IsUnique();
            row.HasIndex(entity => new { entity.UserId, entity.ExpiresAt });
            row.HasIndex(entity => entity.FamilyId);
        });

        modelBuilder.Entity<RecoveryTokenRow>(row =>
        {
            row.ToTable("recovery_tokens");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.TokenHash).HasMaxLength(64).IsRequired().IsFixedLength();
            row.HasIndex(entity => entity.TokenHash).IsUnique();
            row.HasIndex(entity => new { entity.UserId, entity.ExpiresAt });
        });
    }

    private void ConfigureWork(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WorkItemRow>(row =>
        {
            row.ToTable("work_items");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Key).HasMaxLength(64).IsRequired();
            row.Property(entity => entity.Title).HasMaxLength(500).IsRequired();
            row.Property(entity => entity.Goal).HasMaxLength(4000).IsRequired();
            row.Property(entity => entity.InScope).HasMaxLength(4000);
            row.Property(entity => entity.Exclusions).HasMaxLength(4000);
            JsonColumn(row.Property(entity => entity.Stakeholders));
            JsonColumn(row.Property(entity => entity.RelatedModules));

            // A work item key is unique within a project, not globally: two projects may both
            // have a DEV-101 and they are different work.
            row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId, entity.Key }).IsUnique();
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

    private void ConfigureKnowledge(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnowledgeRecordRow>(row =>
        {
            row.ToTable("knowledge_records");
            row.HasKey(entity => entity.Id);
            ScopedIndex(row);
            row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId, entity.WorkItemId });
            row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId, entity.Status });

            row.HasMany(entity => entity.Revisions)
                .WithOne()
                .HasForeignKey(revision => revision.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            row.HasMany(entity => entity.Approvals)
                .WithOne()
                .HasForeignKey(approval => approval.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            row.HasMany(entity => entity.Corrections)
                .WithOne()
                .HasForeignKey(correction => correction.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

        modelBuilder.Entity<RecordRevisionRow>(row =>
        {
            row.ToTable("record_revisions");
            row.HasKey(entity => new { entity.RecordId, entity.Number });
            row.Property(entity => entity.Title).HasMaxLength(500).IsRequired();
            row.Property(entity => entity.Body).IsRequired();
            row.Property(entity => entity.ContentHash).HasMaxLength(64).IsRequired().IsFixedLength();
            JsonColumn(row.Property(entity => entity.FrontMatter));
            JsonColumn(row.Property(entity => entity.Provenance));

            // PostgreSQL computes and stores this. Full-text search and structured filters only:
            // no embeddings and no vector column in v1.
            row.Property(entity => entity.SearchVector)
                .HasColumnType("tsvector")
                .HasComputedColumnSql(
                    "to_tsvector('english', coalesce(title, '') || ' ' || coalesce(body, ''))",
                    stored: true);

            row.HasIndex(entity => entity.SearchVector).HasMethod("gin");
            row.HasIndex(entity => entity.ContentHash);
        });

        modelBuilder.Entity<RecordApprovalRow>(row =>
        {
            row.ToTable("record_approvals");
            row.HasKey(entity => new { entity.RecordId, entity.Sequence });
            row.Property(entity => entity.ApprovedContentHash).HasMaxLength(64).IsRequired().IsFixedLength();
            row.HasIndex(entity => entity.ApprovedContentHash);
        });

        modelBuilder.Entity<RecordCorrectionRow>(row =>
        {
            row.ToTable("record_correction_requests");
            row.HasKey(entity => new { entity.RecordId, entity.Sequence });
            row.Property(entity => entity.Reason).HasMaxLength(4000).IsRequired();
        });
    }

    private void ConfigureEvidenceAndAudit(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvidenceObjectRow>(row =>
        {
            row.ToTable("evidence_objects");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.ContentHash).HasMaxLength(64).IsRequired().IsFixedLength();
            row.Property(entity => entity.MediaType).HasMaxLength(200).IsRequired();
            row.Property(entity => entity.StorageKey).HasMaxLength(1000).IsRequired();
            ScopedIndex(row);
            row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId, entity.ContentHash });
            row.HasQueryFilter(entity => entity.WorkspaceId == CurrentWorkspace);
        });

        modelBuilder.Entity<AuditEventRow>(row =>
        {
            row.ToTable("audit_events");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.ResourceReference).HasMaxLength(500).IsRequired();
            JsonColumn(row.Property(entity => entity.Details));
            row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId, entity.OccurredAt });
            row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId, entity.Action, entity.OccurredAt });
            row.HasIndex(entity => new { entity.ActorId, entity.OccurredAt });

            // Not filtered: system-wide entries have no workspace at all, and a fail-closed
            // filter would hide them from the administrator who needs them. Reads go through
            // ReadAuditHistory, which is scoped and needs its own permission.
        });
    }

    private static void ScopedIndex<TRow>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TRow> row)
        where TRow : class, ITenantScopedRow =>
        row.HasIndex(entity => new { entity.WorkspaceId, entity.ProjectId });

    /// <summary>
    /// Stores a value as jsonb. The comparer matters: without it EF cannot tell whether a mutable
    /// collection changed, and updates are silently lost.
    /// </summary>
    private static void JsonColumn<TValue>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<TValue> property)
        where TValue : class
    {
        var converter = new ValueConverter<TValue, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<TValue>(json, JsonOptions)!);

        var comparer = new ValueComparer<TValue>(
            (left, right) => JsonSerializer.Serialize(left, JsonOptions)
                == JsonSerializer.Serialize(right, JsonOptions),
            value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!);

        property.HasConversion(converter, comparer).HasColumnType("jsonb");
    }

    /// <summary>
    /// PostgreSQL folds unquoted identifiers to lower case, so PascalCase names would have to be
    /// quoted in every hand-written query and every psql session. Twenty lines here save that
    /// everywhere else.
    /// </summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (IMutableKey key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }

            foreach (IMutableIndex index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];

            // A boundary is a lower-to-upper transition, or the end of an acronym running into a
            // word. Without the second case, the EF index prefix IX becomes i_x.
            bool followsWord = index > 0
                && name[index - 1] != '_'
                && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1]));

            bool endsAcronym = index > 0
                && index + 1 < name.Length
                && char.IsUpper(name[index - 1])
                && char.IsLower(name[index + 1]);

            if (char.IsUpper(character) && (followsWord || endsAcronym))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
