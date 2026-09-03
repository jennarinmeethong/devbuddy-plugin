using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Mapping;
using DevBuddy.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Round-tripping every aggregate through real PostgreSQL, and the isolation that has to hold
/// while it happens.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    /// <summary>These tests are bounded by the container lifetime, not by per-call cancellation.</summary>
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task the_migration_creates_every_table_the_model_needs()
    {
        await using DevBuddyDbContext context = _fixture.CreateContext(WorkspaceId.New());

        List<string> tables = await context.Database
            .SqlQuery<string>($"select table_name from information_schema.tables where table_schema = 'public'")
            .ToListAsync();

        string[] expected =
        [
            "workspaces", "teams", "projects", "source_repositories", "deployment_environments",
            "source_snapshots", "users", "memberships", "project_ai_access_policies", "work_items",
            "knowledge_records", "record_revisions", "record_approvals", "record_correction_requests",
            "evidence_objects", "audit_events",
        ];

        foreach (string table in expected)
        {
            Assert.Contains(table, tables);
        }
    }

    [Fact]
    public async Task a_work_item_round_trips_with_its_exclusions_and_stakeholders()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem written = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-1");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);

        WorkItem? read = await repository.FindWorkItemAsync(written.Id, seed.Alpha, Ct);

        Assert.NotNull(read);
        Assert.Equal(written.Key, read.Key);

        // change_request survives as its own type. It is never collapsed into a shared
        // abbreviation, in the schema any more than in the code.
        Assert.Equal(WorkItemType.ChangeRequest, read.Type);
        Assert.Equal("The reporting pipeline, handled separately.", read.Exclusions);
        Assert.Equal("Jennarin", Assert.Single(read.Stakeholders).Name);
        Assert.Equal("src/importer", Assert.Single(read.RelatedModules).ModulePath);
    }

    [Fact]
    public async Task a_published_record_round_trips_with_its_whole_history()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-2");
        var reviewer = UserId.New();

        KnowledgeRecord written = seed.NewPublished(
            seed.Alpha, item.Id, "Why the import runs first", "The importer normalises first.", reviewer);

        await using (DevBuddyDbContext writeContext = _fixture.CreateContext(seed.Workspace))
        {
            await new KnowledgeRepository(writeContext).AddRecordAsync(written, Ct);
        }

        await using DevBuddyDbContext readContext = _fixture.CreateContext(seed.Workspace);
        KnowledgeRecord? read = await new KnowledgeRepository(readContext)
            .FindRecordAsync(written.Id, seed.Alpha, Ct);

        Assert.NotNull(read);
        Assert.Equal(RecordStatus.Published, read.Status);
        Assert.Equal(2, read.Revisions.Count);
        Assert.Equal(2, read.PublishedRevisionNumber);

        // The approval still binds to the exact content it covered. That is the whole point of
        // storing the hash rather than recomputing it (SB-23).
        Approval approval = Assert.Single(read.Approvals);
        Assert.Equal(written.Approvals[0].ApprovedContentHash, approval.ApprovedContentHash);
        Assert.Equal(read.CurrentRevision.ContentHash, approval.ApprovedContentHash);
        Assert.Equal(reviewer, approval.ApproverId);
        Assert.False(approval.ApproverWasDraftCreator);

        // The correction and its reason are part of the record, not a log line that scrolled away.
        Assert.Equal("The rollback step is missing.", Assert.Single(read.CorrectionRequests).Reason);

        // Front matter and provenance survive the jsonb round trip.
        Assert.Equal("Jennarin", read.CurrentRevision.FrontMatter["owner"]);
        Assert.Equal(ProvenanceSourceKind.HumanAuthored, read.CurrentRevision.Provenance.SourceKind);
    }

    [Fact]
    public async Task the_stored_content_hash_matches_the_hash_recomputed_on_load()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-3");
        KnowledgeRecord written = seed.NewDraft(seed.Alpha, item.Id, "Title", "Body with detail.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);
        await repository.AddRecordAsync(written, Ct);

        string stored = await context.RecordRevisions
            .Where(revision => revision.RecordId == written.Id.Value)
            .Select(revision => revision.ContentHash)
            .SingleAsync(Ct);

        KnowledgeRecord? read = await repository.FindRecordAsync(
            written.Id, seed.Alpha, Ct);

        // The hash is recomputed from the title, front matter, and body on construction. If it
        // disagreed with the stored value, something had rewritten a revision in place.
        Assert.NotNull(read);
        Assert.Equal(stored, read.CurrentRevision.ContentHash.Value);
    }

    [Fact]
    public async Task a_record_in_another_project_is_not_found()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Beta, "CRQ-4");
        KnowledgeRecord inBeta = seed.NewDraft(seed.Beta, item.Id, "Beta secret", "Only Beta may see this.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);
        await repository.AddRecordAsync(inBeta, Ct);

        // Same workspace, same user, different project. Isolation is per project, not per tenant.
        Assert.Null(await repository.FindRecordAsync(inBeta.Id, seed.Alpha, Ct));
        Assert.NotNull(await repository.FindRecordAsync(inBeta.Id, seed.Beta, Ct));

        Assert.Empty(await repository.ListRecordsForWorkItemAsync(
            item.Id, seed.Alpha, Ct));
    }

    [Fact]
    public async Task a_query_with_no_workspace_context_returns_nothing()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-5");

        await using (DevBuddyDbContext scoped = _fixture.CreateContext(seed.Workspace))
        {
            Assert.NotNull(await new KnowledgeRepository(scoped)
                .FindWorkItemAsync(item.Id, seed.Alpha, Ct));
        }

        // Fail closed. Unscoped means nothing, not everything: a query somebody adds later and
        // forgets to scope returns an empty result rather than the whole table.
        await using DevBuddyDbContext unscoped = _fixture.CreateContext(workspaceId: null);
        Assert.Null(await new KnowledgeRepository(unscoped)
            .FindWorkItemAsync(item.Id, seed.Alpha, Ct));
    }

    [Fact]
    public async Task a_workspace_context_for_another_workspace_sees_nothing()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-6");

        await using DevBuddyDbContext otherTenant = _fixture.CreateContext(WorkspaceId.New());

        Assert.Null(await new KnowledgeRepository(otherTenant)
            .FindWorkItemAsync(item.Id, seed.Alpha, Ct));
    }

    [Fact]
    public async Task rewriting_a_stored_revision_is_refused()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-7");
        KnowledgeRecord record = seed.NewDraft(seed.Alpha, item.Id, "Title", "Original body.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);
        await repository.AddRecordAsync(record, Ct);

        // A record whose revision 1 says something different. The domain would never produce
        // this; a bug in a use case, or a bad merge, could.
        KnowledgeRecord tampered = seed.NewDraft(seed.Alpha, item.Id, "Title", "Rewritten body.");
        KnowledgeRecord forged = KnowledgeRecord.Rehydrate(
            record.Id,
            seed.Alpha,
            item.Id,
            record.Kind,
            RecordStatus.Draft,
            record.CreatedBy,
            Seed.Now,
            archivedAt: null,
            publishedRevisionNumber: null,
            tampered.Revisions,
            [],
            []);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpdateRecordAsync(forged, Ct));

        // Control SB-24 at the storage layer, where a bug above it cannot get past.
        Assert.Contains("immutable", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task appending_a_revision_and_an_approval_updates_the_record_in_place()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-8");
        KnowledgeRecord record = seed.NewDraft(seed.Alpha, item.Id, "Title", "First body.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        var repository = new KnowledgeRepository(context);
        await repository.AddRecordAsync(record, Ct);

        record.AddRevision(
            "Title",
            "Second body.",
            null,
            new Provenance(ProvenanceSourceKind.HumanAuthored, "review", "Jennarin", Seed.Now),
            Seed.Now.AddMinutes(10),
            seed.Author);

        record.SubmitForApproval(Seed.Now.AddMinutes(11));
        record.Approve(seed.Author, record.CurrentRevision.ContentHash, Seed.Now.AddMinutes(12));
        record.Publish(Seed.Now.AddMinutes(13));

        await repository.UpdateRecordAsync(record, Ct);

        await using DevBuddyDbContext fresh = _fixture.CreateContext(seed.Workspace);
        KnowledgeRecord? read = await new KnowledgeRepository(fresh)
            .FindRecordAsync(record.Id, seed.Alpha, Ct);

        Assert.NotNull(read);
        Assert.Equal(2, read.Revisions.Count);
        Assert.Equal(2, read.PublishedRevisionNumber);
        Assert.Equal("First body.", read.Revisions[0].Body);
        Assert.True(Assert.Single(read.Approvals).ApproverWasDraftCreator);
    }

    [Fact]
    public async Task a_stored_record_claiming_a_published_revision_that_is_absent_fails_on_load()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        WorkItem item = await seed.AddWorkItemAsync(_fixture, seed.Alpha, "CRQ-9");
        KnowledgeRecord record = seed.NewDraft(seed.Alpha, item.Id, "Title", "Body.");

        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);
        await new KnowledgeRepository(context).AddRecordAsync(record, Ct);

        // Simulate drift: the row claims revision 9 is live, but only revision 1 exists.
        await context.Database.ExecuteSqlAsync(
            $"update knowledge_records set published_revision_number = 9 where id = {record.Id.Value}");

        await using DevBuddyDbContext fresh = _fixture.CreateContext(seed.Workspace);

        DomainValidationException failure = await Assert.ThrowsAsync<DomainValidationException>(
            () => new KnowledgeRepository(fresh).FindRecordAsync(
                record.Id, seed.Alpha, Ct));

        // Loud at load, rather than quiet until something tries to publish.
        Assert.Contains("no such revision exists", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task memberships_projects_policies_evidence_and_audit_all_round_trip()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);

        var directory = new AccessDirectory(context);
        var projects = new ProjectDirectory(context, new FileSystemEvidenceBlobStore(Options.Create(new EvidenceStoreOptions())));
        var audit = new AuditStore(context);
        var evidence = new EvidenceMetadataStore(context);

        Membership projectGrant = Membership.ForProject(
            MembershipId.New(), seed.Author, seed.Alpha, Role.Reviewer, Seed.Now, seed.Author);

        await directory.AddMembershipAsync(projectGrant, Ct);

        Membership? readGrant = await directory.FindMembershipAsync(
            projectGrant.Id, Ct);

        Assert.NotNull(readGrant);
        Assert.True(readGrant.Covers(seed.Alpha));
        Assert.False(readGrant.Covers(seed.Beta));

        // A project grant lists that project and no other.
        IReadOnlyList<Project> visible = await projects.ListProjectsForUserAsync(
            seed.Workspace, seed.Author, Ct);

        Assert.Equal(seed.Alpha.ProjectId, Assert.Single(visible).Id);

        ProjectAiAccessPolicy policy = await directory.GetAiAccessPolicyAsync(
            seed.Alpha, Ct);

        // No row yet, and the answer is still deny.
        Assert.False(policy.IsEnabled);

        policy.Enable(seed.Author, Seed.Now, "Sanitised issue exports only.");
        await directory.SaveAiAccessPolicyAsync(policy, Ct);

        ProjectAiAccessPolicy reloaded = await directory.GetAiAccessPolicyAsync(
            seed.Alpha, Ct);

        Assert.True(reloaded.IsEnabled);
        Assert.Equal("Sanitised issue exports only.", reloaded.BoundedDataScope);

        var artefact = new EvidenceObject(
            EvidenceObjectId.New(), seed.Alpha, ContentHash.FromContent("log bytes"),
            "text/plain", 9, "alpha/ab/abc", Seed.Now, seed.Author);

        await evidence.SaveAsync(artefact, Ct);

        EvidenceObject? storedEvidence = await evidence.FindAsync(
            artefact.Id, seed.Alpha, Ct);

        Assert.NotNull(storedEvidence);
        Assert.Equal(RedactionState.NotScanned, storedEvidence.RedactionState);
        Assert.False(storedEvidence.IsReleasable);
        Assert.Empty(await evidence.ListForScopeAsync(seed.Beta, Ct));

        await audit.WriteAsync(
            AuditEvent.ForProject(
                AuditEventId.New(), seed.Alpha, seed.Author, AuditAction.RecordViewed,
                AuditOutcome.Succeeded, "get_record:123", Seed.Now),
            Ct);

        IReadOnlyList<AuditEvent> entries = await audit.QueryAsync(
            seed.Alpha, Seed.Now.AddDays(-1), Seed.Now.AddDays(1), null, Ct);

        Assert.Equal("get_record:123", Assert.Single(entries).ResourceReference);
        Assert.Empty(await audit.QueryAsync(
            seed.Beta, Seed.Now.AddDays(-1), Seed.Now.AddDays(1), null, Ct));
    }

    [Fact]
    public async Task a_revoked_membership_stops_listing_the_project()
    {
        Seed seed = await Seed.CreateAsync(_fixture);
        await using DevBuddyDbContext context = _fixture.CreateContext(seed.Workspace);

        var directory = new AccessDirectory(context);
        var projects = new ProjectDirectory(context, new FileSystemEvidenceBlobStore(Options.Create(new EvidenceStoreOptions())));

        Membership grant = Membership.ForWorkspace(
            MembershipId.New(), seed.Author, seed.Workspace, Role.Contributor, Seed.Now, seed.Author);

        await directory.AddMembershipAsync(grant, Ct);

        Assert.Equal(2, (await projects.ListProjectsForUserAsync(
            seed.Workspace, seed.Author, Ct)).Count);

        grant.Revoke(Seed.Now.AddDays(1));
        await directory.UpdateMembershipAsync(grant, Ct);

        // Revocation takes effect on the next request. It does not recall what was already
        // taken, which is accepted limitation AL-3.
        Assert.Empty(await projects.ListProjectsForUserAsync(
            seed.Workspace, seed.Author, Ct));
    }
}
