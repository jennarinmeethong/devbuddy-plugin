namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// The kinds of knowledge this system retains. Each member maps to one requirement in the
/// Knowledge Records section of info.md, so that section can be checked against this enum.
/// <para>
/// CodeReviewFeedback is a record kind in its own right, separate from a change request. The
/// two are never merged behind a shared abbreviation.
/// </para>
/// </summary>
public enum RecordKind
{
    /// <summary>Links to issues, pull requests, commits, documents, environments, configuration.</summary>
    ContextReference = 1,

    /// <summary>Status, owner, last update, acceptance criteria, verification method, evidence.</summary>
    DeliveryState = 2,

    /// <summary>The decision, its rationale, alternatives considered, approver, decision date.</summary>
    Decision = 3,

    /// <summary>Architecture, data flows, APIs and contracts, dependencies, important configuration.</summary>
    TechnicalKnowledge = 4,

    /// <summary>Affected files, migration or rollback, limitations, risks, bugs, root causes, workarounds.</summary>
    ChangeImpact = 5,

    /// <summary>Remaining work, open questions, cautions, practical next steps for the next owner.</summary>
    Handover = 6,

    /// <summary>Review feedback, resolution status, and the rationale when feedback is not applied.</summary>
    CodeReviewFeedback = 7,
}
