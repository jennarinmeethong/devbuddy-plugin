namespace DevBuddy.Domain.Common;

/// <summary>
/// Strongly typed identifiers. These exist for one reason: the most damaging bug class in a
/// multi-tenant system is passing the wrong identifier to the right parameter. A bare Guid
/// makes that a runtime data leak; a distinct type makes it a compile error.
/// </summary>
public readonly record struct WorkspaceId(Guid Value)
{
    public static WorkspaceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct TeamId(Guid Value)
{
    public static TeamId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct SourceRepositoryId(Guid Value)
{
    public static SourceRepositoryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct DeploymentEnvironmentId(Guid Value)
{
    public static DeploymentEnvironmentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct MembershipId(Guid Value)
{
    public static MembershipId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct WorkItemId(Guid Value)
{
    public static WorkItemId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct KnowledgeRecordId(Guid Value)
{
    public static KnowledgeRecordId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct EvidenceObjectId(Guid Value)
{
    public static EvidenceObjectId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct AuditEventId(Guid Value)
{
    public static AuditEventId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
