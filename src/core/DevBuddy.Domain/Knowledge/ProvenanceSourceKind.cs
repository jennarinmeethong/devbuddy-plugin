namespace DevBuddy.Domain.Knowledge;

/// <summary>
/// Where the content of a revision came from. Recorded so a reader can tell analysis output
/// from a human statement, and so an imported claim can be re-checked against its origin.
/// </summary>
public enum ProvenanceSourceKind
{
    HumanAuthored = 1,
    RepositoryAnalysis = 2,
    GitHistory = 3,
    IssueTracker = 4,
    PullRequest = 5,
    Document = 6,
    TestEvidence = 7,
    AiDraft = 8,
}
