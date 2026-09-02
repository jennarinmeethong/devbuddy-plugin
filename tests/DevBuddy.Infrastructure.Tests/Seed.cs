using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Builds a workspace with two projects and writes it to the database.
/// <para>
/// Two projects, always. Every isolation test needs a second project that the query under test
/// must not reach, and a fixture with only one project would let an isolation bug pass.
/// </para>
/// </summary>
internal sealed class Seed
{
    private Seed(WorkspaceId workspace, ProjectId alpha, ProjectId beta, UserId author)
    {
        Workspace = workspace;
        Alpha = new ProjectScope(workspace, alpha);
        Beta = new ProjectScope(workspace, beta);
        Author = author;
    }

    public WorkspaceId Workspace { get; }

    public ProjectScope Alpha { get; }

    public ProjectScope Beta { get; }

    public UserId Author { get; }

    public static DateTimeOffset Now { get; } = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public static async Task<Seed> CreateAsync(PostgresFixture fixture)
    {
        var workspace = WorkspaceId.New();
        var seed = new Seed(workspace, ProjectId.New(), ProjectId.New(), UserId.New());

        await using DevBuddyDbContext context = fixture.CreateContext(workspace);

        context.Workspaces.Add(RowMappers.ToRow(
            new Workspace(workspace, "Acme", seed.Author, Now)));

        // Unique per seed: email is globally unique, and every test builds its own workspace.
        context.Users.Add(RowMappers.ToRow(
            new User(seed.Author, $"author-{seed.Author.Value:N}@example.com", "Jennarin", Now)));

        context.Projects.Add(RowMappers.ToRow(
            new Project(seed.Alpha.ProjectId, workspace, "Alpha", Now)));

        context.Projects.Add(RowMappers.ToRow(
            new Project(seed.Beta.ProjectId, workspace, "Beta", Now)));

        await context.SaveChangesAsync();
        return seed;
    }

    public WorkItem NewWorkItem(ProjectScope scope, string key) =>
        new(WorkItemId.New(), scope, key, WorkItemType.ChangeRequest,
            "Import normalisation", "Identifiers are normalised before validation runs.", Now, Author);

    public async Task<WorkItem> AddWorkItemAsync(PostgresFixture fixture, ProjectScope scope, string key)
    {
        WorkItem item = NewWorkItem(scope, key);
        item.SetScope("The importer.", "The reporting pipeline, handled separately.");
        item.AddStakeholder(new Stakeholder("Jennarin", StakeholderRole.Owner, "owner@example.com"));
        item.AddRelatedModule(new RelatedModule(SourceRepositoryId.New(), "src/importer"));

        await using DevBuddyDbContext context = fixture.CreateContext(scope.WorkspaceId);
        context.WorkItems.Add(RowMappers.ToRow(item));
        await context.SaveChangesAsync();
        return item;
    }

    public KnowledgeRecord NewDraft(ProjectScope scope, WorkItemId workItemId, string title, string body) =>
        KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(),
            scope,
            workItemId,
            RecordKind.Decision,
            title,
            body,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["owner"] = "Jennarin" },
            new Provenance(ProvenanceSourceKind.HumanAuthored, "handover/2026-09-01", "Jennarin", Now),
            Now,
            Author);

    /// <summary>A record taken all the way to published, with a correction in its history.</summary>
    public KnowledgeRecord NewPublished(
        ProjectScope scope, WorkItemId workItemId, string title, string body, UserId reviewer)
    {
        KnowledgeRecord record = NewDraft(scope, workItemId, title, "First attempt.");
        record.SubmitForApproval(Now.AddMinutes(1));
        record.RequestCorrection(reviewer, "The rollback step is missing.", Now.AddMinutes(2));

        record.AddRevision(
            title,
            body,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["owner"] = "Jennarin" },
            new Provenance(ProvenanceSourceKind.HumanAuthored, "handover/2026-09-01", "Jennarin", Now),
            Now.AddMinutes(3),
            Author);

        record.SubmitForApproval(Now.AddMinutes(4));
        record.Approve(reviewer, record.CurrentRevision.ContentHash, Now.AddMinutes(5));
        record.Publish(Now.AddMinutes(6));
        return record;
    }
}
