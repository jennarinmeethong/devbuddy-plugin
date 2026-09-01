using DevBuddy.Domain.Common;
using DevBuddy.Domain.Work;

namespace DevBuddy.Domain.Tests;

/// <summary>
/// Work identity. The type enum is the part info.md is most specific about: Change Request and
/// Code Review are separate concepts, and the abbreviation CR is not used for either.
/// </summary>
public sealed class WorkItemTests
{
    private static WorkItem Create(WorkItemType type = WorkItemType.Develop) =>
        new(
            WorkItemId.New(),
            Fixtures.AlphaScope,
            key: "DEV-101",
            type,
            title: "Import normalisation",
            goal: "Identifiers are normalised before validation runs.",
            createdAt: Fixtures.Now,
            createdBy: Fixtures.Author);

    [Fact]
    public void change_request_and_code_review_are_distinct_work_item_types()
    {
        Assert.NotEqual(WorkItemType.ChangeRequest, WorkItemType.CodeReview);

        WorkItemType[] all = Enum.GetValues<WorkItemType>();
        Assert.Contains(WorkItemType.ChangeRequest, all);
        Assert.Contains(WorkItemType.CodeReview, all);
        Assert.Equal(5, all.Length);
    }

    [Fact]
    public void the_work_item_type_enum_has_no_zero_member()
    {
        // A default(WorkItemType) is not a valid work item, so there is deliberately no member
        // representing "no type". Guard.Defined rejects it rather than letting it persist.
        Assert.False(Enum.IsDefined((WorkItemType)0));
        Assert.Throws<DomainValidationException>(() => Create((WorkItemType)0));
    }

    [Fact]
    public void a_work_item_records_scope_exclusions_and_stakeholders()
    {
        WorkItem item = Create(WorkItemType.ChangeRequest);

        item.SetScope(
            inScope: "The importer and its validator.",
            exclusions: "The reporting pipeline, which is handled separately.");

        item.AddStakeholder(new Stakeholder("Jennarin", StakeholderRole.Owner));
        item.AddStakeholder(new Stakeholder("Ops team", StakeholderRole.Informed, "ops@example.com"));

        Assert.Equal("The importer and its validator.", item.InScope);
        Assert.Contains("reporting pipeline", item.Exclusions, StringComparison.Ordinal);
        Assert.Equal(2, item.Stakeholders.Count);
    }

    [Fact]
    public void adding_the_same_stakeholder_twice_does_not_duplicate_it()
    {
        WorkItem item = Create();
        var owner = new Stakeholder("Jennarin", StakeholderRole.Owner);

        item.AddStakeholder(owner);
        item.AddStakeholder(new Stakeholder("Jennarin", StakeholderRole.Owner));

        Assert.Single(item.Stakeholders);

        item.RemoveStakeholder(owner);
        Assert.Empty(item.Stakeholders);
    }

    [Fact]
    public void a_work_item_requires_a_key_a_title_and_a_goal()
    {
        Assert.Throws<DomainValidationException>(() => new WorkItem(
            WorkItemId.New(), Fixtures.AlphaScope, "  ", WorkItemType.Develop, "Title", "Goal",
            Fixtures.Now, Fixtures.Author));

        Assert.Throws<DomainValidationException>(() => new WorkItem(
            WorkItemId.New(), Fixtures.AlphaScope, "DEV-1", WorkItemType.Develop, "", "Goal",
            Fixtures.Now, Fixtures.Author));

        Assert.Throws<DomainValidationException>(() => new WorkItem(
            WorkItemId.New(), Fixtures.AlphaScope, "DEV-1", WorkItemType.Develop, "Title", "   ",
            Fixtures.Now, Fixtures.Author));
    }

    [Fact]
    public void a_related_module_requires_a_repository()
    {
        Assert.Throws<DomainValidationException>(() => new RelatedModule(default));

        var module = new RelatedModule(SourceRepositoryId.New(), "src/importer");
        Assert.Equal("src/importer", module.ModulePath);
    }
}
