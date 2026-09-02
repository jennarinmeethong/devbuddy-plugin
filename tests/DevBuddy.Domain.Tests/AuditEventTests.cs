using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;

namespace DevBuddy.Domain.Tests;

/// <summary>
/// Audit entries. Amended in Phase 2 to carry a nullable workspace and project rather than a
/// <see cref="Tenancy.ProjectScope"/>, because real auditable actions happen above a project:
/// listing projects, granting membership, taking a backup.
/// </summary>
public sealed class AuditEventTests
{
    [Fact]
    public void a_project_scoped_entry_carries_both_workspace_and_project()
    {
        AuditEvent entry = AuditEvent.ForProject(
            AuditEventId.New(), Fixtures.AlphaScope, Fixtures.Author,
            AuditAction.RecordViewed, AuditOutcome.Succeeded, "record:123", Fixtures.Now);

        Assert.Equal(Fixtures.Workspace, entry.WorkspaceId);
        Assert.Equal(Fixtures.ProjectAlpha, entry.ProjectId);
    }

    [Fact]
    public void a_workspace_entry_has_no_project_and_a_system_entry_has_neither()
    {
        AuditEvent workspace = AuditEvent.ForWorkspace(
            AuditEventId.New(), Fixtures.Workspace, Fixtures.Author,
            AuditAction.MembershipGranted, AuditOutcome.Succeeded, "user:456", Fixtures.Now);

        AuditEvent system = AuditEvent.ForSystem(
            AuditEventId.New(), Fixtures.Author,
            AuditAction.BackupCreated, AuditOutcome.Succeeded, "backup-1", Fixtures.Now);

        Assert.Equal(Fixtures.Workspace, workspace.WorkspaceId);
        Assert.Null(workspace.ProjectId);
        Assert.Null(system.WorkspaceId);
        Assert.Null(system.ProjectId);
    }

    [Fact]
    public void a_project_cannot_be_recorded_without_its_workspace()
    {
        // Otherwise an entry would name a project nobody can place, which defeats the point of
        // auditing a multi-tenant system at all.
        DomainValidationException failure = Assert.Throws<DomainValidationException>(() =>
            new AuditEvent(
                AuditEventId.New(), workspaceId: null, projectId: Fixtures.ProjectAlpha,
                Fixtures.Author, AuditAction.RecordViewed, AuditOutcome.Succeeded,
                "record:123", Fixtures.Now));

        Assert.Contains("workspace", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void a_denial_is_recorded_as_deliberately_as_a_success()
    {
        AuditEvent denied = AuditEvent.ForProject(
            AuditEventId.New(), Fixtures.AlphaScope, Fixtures.Author,
            AuditAction.AccessDenied, AuditOutcome.Denied, "get_record:123", Fixtures.Now);

        // A refused cross-project read is exactly the event an investigation needs to find.
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(AuditAction.AccessDenied, denied.Action);
    }

    [Fact]
    public void detail_values_are_capped_so_the_audit_cannot_become_a_copy_of_the_content()
    {
        var oversized = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["body"] = new('x', 201),
        };

        // Refused, not truncated. A truncated secret is still a leak, and a refusal is visible
        // (SB-19).
        DomainValidationException failure = Assert.Throws<DomainValidationException>(() =>
            AuditEvent.ForProject(
                AuditEventId.New(), Fixtures.AlphaScope, Fixtures.Author, AuditAction.RecordApproved,
                AuditOutcome.Succeeded, "record:123", Fixtures.Now, oversized));

        Assert.Contains("body", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void an_entry_carries_at_most_twenty_details()
    {
        var tooMany = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < 21; index++)
        {
            tooMany[$"key{index}"] = "value";
        }

        Assert.Throws<DomainValidationException>(() =>
            AuditEvent.ForProject(
                AuditEventId.New(), Fixtures.AlphaScope, Fixtures.Author, AuditAction.RecordApproved,
                AuditOutcome.Succeeded, "record:123", Fixtures.Now, tooMany));
    }

    [Fact]
    public void an_approval_entry_carries_the_four_things_info_md_asks_for()
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["approver"] = Fixtures.Reviewer.ToString(),
            ["approved_revision"] = "2",
            ["approved_content_hash"] = ContentHash.FromContent("body").Value,
            ["approved_at"] = Fixtures.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["approver_was_draft_creator"] = "false",
        };

        AuditEvent entry = AuditEvent.ForProject(
            AuditEventId.New(), Fixtures.AlphaScope, Fixtures.Reviewer, AuditAction.RecordApproved,
            AuditOutcome.Succeeded, "approve_record:123", Fixtures.Now, details);

        Assert.Equal("2", entry.Details["approved_revision"]);
        Assert.Equal(64, entry.Details["approved_content_hash"].Length);
        Assert.Equal("false", entry.Details["approver_was_draft_creator"]);
    }

    [Fact]
    public void an_entry_with_no_details_has_an_empty_bag_rather_than_null()
    {
        AuditEvent entry = AuditEvent.ForSystem(
            AuditEventId.New(), Fixtures.Author, AuditAction.HealthChecked,
            AuditOutcome.Succeeded, "system", Fixtures.Now);

        Assert.Empty(entry.Details);
    }

    [Fact]
    public void an_entry_requires_a_resource_reference_and_a_utc_timestamp()
    {
        Assert.Throws<DomainValidationException>(() => AuditEvent.ForSystem(
            AuditEventId.New(), Fixtures.Author, AuditAction.BackupCreated,
            AuditOutcome.Succeeded, "   ", Fixtures.Now));

        Assert.Throws<DomainValidationException>(() => AuditEvent.ForSystem(
            AuditEventId.New(), Fixtures.Author, AuditAction.BackupCreated,
            AuditOutcome.Succeeded, "backup-1",
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(7))));
    }
}
