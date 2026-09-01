using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Domain.Work;

/// <summary>
/// Work identity: the anchor every knowledge record hangs off. DevBuddy owns this, and GitHub
/// issues and pull requests are imported read-only as linked references. See ADR-0010.
/// </summary>
public sealed class WorkItem
{
    private readonly List<Stakeholder> _stakeholders = [];
    private readonly List<RelatedModule> _relatedModules = [];

    public WorkItem(
        WorkItemId id,
        ProjectScope scope,
        string key,
        WorkItemType type,
        string title,
        string goal,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        Id = id;
        Scope = scope;
        Key = Guard.NotLongerThan(Guard.NotBlank(key, nameof(key)), 64, nameof(key));
        Type = Guard.Defined(type, nameof(type));
        Title = Guard.NotLongerThan(Guard.NotBlank(title, nameof(title)), 500, nameof(title));
        Goal = Guard.NotLongerThan(Guard.NotBlank(goal, nameof(goal)), 4000, nameof(goal));
        CreatedAt = Guard.Utc(createdAt, nameof(createdAt));
        CreatedBy = createdBy;
    }

    public WorkItemId Id { get; }

    public ProjectScope Scope { get; }

    /// <summary>
    /// The human-readable identifier a team actually says out loud, for example DEV-101.
    /// Unique within a project.
    /// </summary>
    public string Key { get; }

    public WorkItemType Type { get; }

    public string Title { get; private set; }

    public string Goal { get; private set; }

    /// <summary>What this work covers.</summary>
    public string? InScope { get; private set; }

    /// <summary>
    /// What this work deliberately does not cover. Recorded separately from scope because the
    /// exclusions are usually what a later owner needs and never finds written down.
    /// </summary>
    public string? Exclusions { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public UserId CreatedBy { get; }

    public IReadOnlyList<Stakeholder> Stakeholders => _stakeholders;

    public IReadOnlyList<RelatedModule> RelatedModules => _relatedModules;

    public void Describe(string title, string goal)
    {
        Title = Guard.NotLongerThan(Guard.NotBlank(title, nameof(title)), 500, nameof(title));
        Goal = Guard.NotLongerThan(Guard.NotBlank(goal, nameof(goal)), 4000, nameof(goal));
    }

    public void SetScope(string? inScope, string? exclusions)
    {
        InScope = inScope is null
            ? null
            : Guard.NotLongerThan(inScope, 4000, nameof(inScope));
        Exclusions = exclusions is null
            ? null
            : Guard.NotLongerThan(exclusions, 4000, nameof(exclusions));
    }

    public void AddStakeholder(Stakeholder stakeholder)
    {
        Guard.NotNull(stakeholder, nameof(stakeholder));

        if (!_stakeholders.Contains(stakeholder))
        {
            _stakeholders.Add(stakeholder);
        }
    }

    public void RemoveStakeholder(Stakeholder stakeholder) => _stakeholders.Remove(stakeholder);

    public void AddRelatedModule(RelatedModule module)
    {
        Guard.NotNull(module, nameof(module));

        if (!_relatedModules.Contains(module))
        {
            _relatedModules.Add(module);
        }
    }

    public void RemoveRelatedModule(RelatedModule module) => _relatedModules.Remove(module);
}
