using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;
using DevBuddy.Domain.Tenancy;
using DevBuddy.Domain.Work;

namespace DevBuddy.Domain.Tests;

/// <summary>
/// Shared construction helpers. Every value is explicit rather than random, so a failing test
/// reports the same thing twice in a row.
/// </summary>
internal static class Fixtures
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public static readonly WorkspaceId Workspace = new(new Guid("11111111-1111-1111-1111-111111111111"));

    public static readonly ProjectId ProjectAlpha = new(new Guid("22222222-2222-2222-2222-222222222222"));

    public static readonly ProjectId ProjectBeta = new(new Guid("33333333-3333-3333-3333-333333333333"));

    public static readonly WorkItemId WorkItem = new(new Guid("44444444-4444-4444-4444-444444444444"));

    public static readonly UserId Author = new(new Guid("55555555-5555-5555-5555-555555555555"));

    public static readonly UserId Reviewer = new(new Guid("66666666-6666-6666-6666-666666666666"));

    public static ProjectScope AlphaScope => new(Workspace, ProjectAlpha);

    public static ProjectScope BetaScope => new(Workspace, ProjectBeta);

    public static Provenance HumanProvenance(DateTimeOffset? at = null) =>
        new(
            ProvenanceSourceKind.HumanAuthored,
            sourceLocator: "handover-session/2026-09-01",
            author: "Jennarin",
            recordedAt: at ?? Now);

    /// <summary>
    /// A record in Draft with one revision. Callers move it through the lifecycle themselves,
    /// so each test states the path it depends on rather than inheriting it.
    /// </summary>
    public static KnowledgeRecord Draft(
        string title = "Why the import runs before validation",
        string body = "The importer must normalise identifiers before the validator sees them.",
        RecordKind kind = RecordKind.Decision) =>
        KnowledgeRecord.CreateDraft(
            KnowledgeRecordId.New(),
            AlphaScope,
            WorkItem,
            kind,
            title,
            body,
            frontMatter: null,
            HumanProvenance(),
            Now,
            Author);

    /// <summary>A record advanced to Approved by the reviewer, ready to publish.</summary>
    public static KnowledgeRecord ApprovedRecord()
    {
        KnowledgeRecord record = Draft();
        record.SubmitForApproval(Now.AddMinutes(1));
        record.Approve(Reviewer, record.CurrentRevision.ContentHash, Now.AddMinutes(2));
        return record;
    }
}
