using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Domain.Tests;

/// <summary>
/// The controlled lifecycle itself: which transitions are allowed, and which are refused.
/// </summary>
public sealed class KnowledgeRecordLifecycleTests
{
    [Fact]
    public void a_new_record_starts_as_a_draft_with_one_revision_and_nothing_published()
    {
        KnowledgeRecord record = Fixtures.Draft();

        Assert.Equal(RecordStatus.Draft, record.Status);
        Assert.Single(record.Revisions);
        Assert.Equal(1, record.CurrentRevision.Number);
        Assert.Null(record.PublishedRevisionNumber);
        Assert.Null(record.PublishedRevision);
        Assert.Empty(record.Approvals);
    }

    [Fact]
    public void publish_is_rejected_when_the_record_was_never_approved()
    {
        KnowledgeRecord record = Fixtures.Draft();

        Assert.Throws<InvalidTransitionException>(() => record.Publish(Fixtures.Now.AddMinutes(1)));
        Assert.Equal(RecordStatus.Draft, record.Status);
        Assert.Null(record.PublishedRevisionNumber);
    }

    [Fact]
    public void approve_is_rejected_while_the_record_is_still_a_draft()
    {
        KnowledgeRecord record = Fixtures.Draft();

        Assert.Throws<InvalidTransitionException>(
            () => record.Approve(Fixtures.Reviewer, record.CurrentRevision.ContentHash, Fixtures.Now.AddMinutes(1)));
        Assert.Empty(record.Approvals);
    }

    [Fact]
    public void submit_for_approval_is_rejected_unless_the_record_is_a_draft()
    {
        KnowledgeRecord record = Fixtures.ApprovedRecord();

        Assert.Throws<InvalidTransitionException>(() => record.SubmitForApproval(Fixtures.Now.AddMinutes(5)));
        Assert.Equal(RecordStatus.Approved, record.Status);
    }

    [Fact]
    public void a_correction_cannot_be_requested_on_a_draft_that_was_never_submitted()
    {
        KnowledgeRecord record = Fixtures.Draft();

        Assert.Throws<InvalidTransitionException>(
            () => record.RequestCorrection(Fixtures.Reviewer, "Not ready.", Fixtures.Now.AddMinutes(1)));
        Assert.Empty(record.CorrectionRequests);
    }

    [Fact]
    public void an_archived_record_refuses_every_change()
    {
        KnowledgeRecord record = Fixtures.ApprovedRecord();
        record.Publish(Fixtures.Now.AddMinutes(3));
        record.Archive(Fixtures.Now.AddMinutes(4));

        Assert.Equal(RecordStatus.Archived, record.Status);
        Assert.Equal(Fixtures.Now.AddMinutes(4), record.ArchivedAt);

        Assert.Throws<InvalidTransitionException>(() => record.SubmitForApproval(Fixtures.Now.AddMinutes(5)));
        Assert.Throws<InvalidTransitionException>(() => record.Publish(Fixtures.Now.AddMinutes(5)));
        Assert.Throws<InvalidTransitionException>(() => record.Archive(Fixtures.Now.AddMinutes(5)));
        Assert.Throws<InvalidTransitionException>(() => record.AddRevision(
            "Anything", "Anything", null, Fixtures.HumanProvenance(), Fixtures.Now.AddMinutes(5), Fixtures.Author));
    }

    [Fact]
    public void a_record_must_belong_to_a_work_item()
    {
        DomainValidationException failure = Assert.Throws<DomainValidationException>(() =>
            KnowledgeRecord.CreateDraft(
                KnowledgeRecordId.New(),
                Fixtures.AlphaScope,
                default,
                RecordKind.Handover,
                "Title",
                "Body",
                frontMatter: null,
                Fixtures.HumanProvenance(),
                Fixtures.Now,
                Fixtures.Author));

        Assert.Contains("work item", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RecordKind.ContextReference)]
    [InlineData(RecordKind.DeliveryState)]
    [InlineData(RecordKind.Decision)]
    [InlineData(RecordKind.TechnicalKnowledge)]
    [InlineData(RecordKind.ChangeImpact)]
    [InlineData(RecordKind.Handover)]
    [InlineData(RecordKind.CodeReviewFeedback)]
    public void every_record_kind_from_info_md_can_be_drafted(RecordKind kind)
    {
        KnowledgeRecord record = Fixtures.Draft(kind: kind);

        Assert.Equal(kind, record.Kind);
    }

    [Fact]
    public void an_undefined_record_kind_is_refused()
    {
        Assert.Throws<DomainValidationException>(() =>
            KnowledgeRecord.CreateDraft(
                KnowledgeRecordId.New(),
                Fixtures.AlphaScope,
                Fixtures.WorkItem,
                (RecordKind)99,
                "Title",
                "Body",
                frontMatter: null,
                Fixtures.HumanProvenance(),
                Fixtures.Now,
                Fixtures.Author));
    }
}
