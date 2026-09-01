using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Domain.Tests;

/// <summary>
/// Control SB-23: approval binds to the exact content revision that was reviewed.
/// These are the tests the Phase 1 exit criteria name explicitly.
/// </summary>
public sealed class KnowledgeRecordApprovalTests
{
    [Fact]
    public void publish_is_rejected_when_approval_is_for_an_older_revision()
    {
        KnowledgeRecord record = Fixtures.ApprovedRecord();
        ContentHash approvedContent = record.CurrentRevision.ContentHash;

        // The text changes after the reviewer read it. This is the attack and the accident.
        record.AddRevision(
            title: "Why the import runs before validation",
            body: "The importer skips normalisation when the feature flag is on.",
            frontMatter: null,
            Fixtures.HumanProvenance(),
            Fixtures.Now.AddMinutes(5),
            Fixtures.Author);

        record.SubmitForApproval(Fixtures.Now.AddMinutes(6));

        Assert.NotEqual(approvedContent, record.CurrentRevision.ContentHash);
        Assert.Null(record.ApprovalForCurrentRevision);

        // Publication is blocked at the state check, and would be blocked by the hash check too.
        InvalidTransitionException transition =
            Assert.Throws<InvalidTransitionException>(() => record.Publish(Fixtures.Now.AddMinutes(7)));
        Assert.Contains("approved record", transition.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RecordStatus.PendingApproval, record.Status);
        Assert.Null(record.PublishedRevisionNumber);
    }

    [Fact]
    public void approve_is_rejected_when_the_hash_is_not_the_current_revision()
    {
        KnowledgeRecord record = Fixtures.Draft();
        ContentHash staleHash = record.CurrentRevision.ContentHash;

        record.AddRevision(
            title: "Why the import runs before validation",
            body: "Revised after review comments.",
            frontMatter: null,
            Fixtures.HumanProvenance(),
            Fixtures.Now.AddMinutes(3),
            Fixtures.Author);

        record.SubmitForApproval(Fixtures.Now.AddMinutes(4));

        ApprovalRevisionMismatchException mismatch = Assert.Throws<ApprovalRevisionMismatchException>(
            () => record.Approve(Fixtures.Reviewer, staleHash, Fixtures.Now.AddMinutes(5)));

        Assert.Contains(staleHash.ToShortString(), mismatch.Message, StringComparison.Ordinal);
        Assert.Empty(record.Approvals);
        Assert.Equal(RecordStatus.PendingApproval, record.Status);
    }

    [Fact]
    public void publish_succeeds_when_the_approval_covers_the_current_revision()
    {
        KnowledgeRecord record = Fixtures.ApprovedRecord();

        record.Publish(Fixtures.Now.AddMinutes(3));

        Assert.Equal(RecordStatus.Published, record.Status);
        Assert.Equal(1, record.PublishedRevisionNumber);
        Assert.NotNull(record.PublishedRevision);
        Assert.Equal(record.CurrentRevision.ContentHash, record.PublishedRevision.ContentHash);
    }

    [Fact]
    public void approval_records_whether_the_approver_was_the_draft_creator()
    {
        KnowledgeRecord selfApproved = Fixtures.Draft();
        selfApproved.SubmitForApproval(Fixtures.Now.AddMinutes(1));
        Approval own = selfApproved.Approve(
            Fixtures.Author, selfApproved.CurrentRevision.ContentHash, Fixtures.Now.AddMinutes(2));

        KnowledgeRecord reviewed = Fixtures.ApprovedRecord();
        Approval byReviewer = reviewed.Approvals.Single();

        // info.md permits self-approval. It requires it to be visible, not prevented.
        Assert.True(own.ApproverWasDraftCreator);
        Assert.False(byReviewer.ApproverWasDraftCreator);
        Assert.Equal(Fixtures.Author, own.ApproverId);
        Assert.Equal(Fixtures.Reviewer, byReviewer.ApproverId);
    }

    [Fact]
    public void approval_history_is_kept_after_a_revision_supersedes_it()
    {
        KnowledgeRecord record = Fixtures.ApprovedRecord();

        record.AddRevision(
            title: "Why the import runs before validation",
            body: "Third draft.",
            frontMatter: null,
            Fixtures.HumanProvenance(),
            Fixtures.Now.AddMinutes(10),
            Fixtures.Author);

        // The old approval is superseded, not erased: it is what the audit trail is made of.
        Assert.Single(record.Approvals);
        Assert.Equal(1, record.Approvals[0].ApprovedRevisionNumber);
        Assert.Null(record.ApprovalForCurrentRevision);
    }

    [Fact]
    public void publishing_a_later_revision_does_not_erase_the_previously_published_one()
    {
        KnowledgeRecord record = Fixtures.ApprovedRecord();
        record.Publish(Fixtures.Now.AddMinutes(3));

        record.AddRevision(
            title: "Why the import runs before validation",
            body: "A draft that is not published yet.",
            frontMatter: null,
            Fixtures.HumanProvenance(),
            Fixtures.Now.AddMinutes(20),
            Fixtures.Author);

        // Control SB-26: a new draft must not replace what readers currently see.
        Assert.Equal(RecordStatus.Draft, record.Status);
        Assert.Equal(1, record.PublishedRevisionNumber);
        Assert.NotNull(record.PublishedRevision);
        Assert.Equal(2, record.CurrentRevision.Number);
        Assert.NotEqual(record.CurrentRevision.Number, record.PublishedRevision.Number);
    }

    [Fact]
    public void a_correction_request_returns_the_record_to_draft_and_keeps_the_reason()
    {
        KnowledgeRecord record = Fixtures.Draft();
        record.SubmitForApproval(Fixtures.Now.AddMinutes(1));

        CorrectionRequest request = record.RequestCorrection(
            Fixtures.Reviewer, "The rollback step is missing.", Fixtures.Now.AddMinutes(2));

        Assert.Equal(RecordStatus.Draft, record.Status);
        Assert.Equal("The rollback step is missing.", request.Reason);
        Assert.Equal(1, request.TargetRevisionNumber);
        Assert.Single(record.CorrectionRequests);
    }
}
