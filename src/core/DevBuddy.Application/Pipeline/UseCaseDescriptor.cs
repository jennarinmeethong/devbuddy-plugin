using DevBuddy.Application.Security;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Everything the pipeline needs to know about a use case before running it: what it is called,
/// what permission it requires, whether AI may reach it, what to audit, and whether its output
/// passes through redaction.
/// <para>
/// This is declared next to the use case rather than configured elsewhere, so adding a use case
/// forces the author to answer all five questions. A use case that quietly requires nothing
/// cannot be written.
/// </para>
/// </summary>
public sealed record UseCaseDescriptor
{
    public UseCaseDescriptor(
        string name,
        PermissionKind permission,
        AiExposure aiExposure,
        AuditAction auditAction,
        bool redactsOutput)
    {
        Name = Guard.NotLongerThan(Guard.NotBlank(name, nameof(name)), 100, nameof(name));
        Permission = Guard.Defined(permission, nameof(permission));
        AiExposure = Guard.Defined(aiExposure, nameof(aiExposure));
        AuditAction = Guard.Defined(auditAction, nameof(auditAction));
        RedactsOutput = redactsOutput;
    }

    /// <summary>The operation name used by the API and, where exposed, by MCP. Snake case.</summary>
    public string Name { get; }

    public PermissionKind Permission { get; }

    public AiExposure AiExposure { get; }

    public AuditAction AuditAction { get; }

    /// <summary>
    /// True when the response carries free text that must pass through the redactor before it
    /// leaves the boundary. A response type that declares this must implement
    /// IRedactableResponse, and a test enforces the pairing.
    /// </summary>
    public bool RedactsOutput { get; }
}
