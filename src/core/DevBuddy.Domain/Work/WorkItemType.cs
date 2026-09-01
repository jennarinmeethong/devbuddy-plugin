namespace DevBuddy.Domain.Work;

/// <summary>
/// The five kinds of work this system records.
/// <para>
/// ChangeRequest and CodeReview are deliberately separate members. info.md requires them to be
/// treated as separate concepts and separate record types, and forbids the abbreviation CR as a
/// shared identifier for both. An architecture test in DevBuddy.Application.Tests fails the build
/// if that abbreviation reappears as an identifier anywhere in this assembly.
/// </para>
/// <para>
/// There is deliberately no zero member. A work item with no type is not a state this domain
/// permits, so default(WorkItemType) is invalid by construction and Guard.Defined rejects it.
/// </para>
/// </summary>
public enum WorkItemType
{
    Develop = 1,
    Enhance = 2,
    FixBug = 3,
    ChangeRequest = 4,
    CodeReview = 5,
}
