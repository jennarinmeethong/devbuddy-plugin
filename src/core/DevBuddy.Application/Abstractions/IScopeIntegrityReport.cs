namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Finds rows written against a project that is not a live project of their own workspace.
/// <para>
/// Until 2026-09-17 authorization checked the caller's membership in the named workspace and never
/// that the project belonged to it, so a workspace administrator could store a work item, a record,
/// evidence, a project grant or an AI policy under their own workspace against another tenant's
/// project identifier, or one that exists nowhere. The check exists now; this reports what an
/// installation may have kept from before it.
/// </para>
/// <para>
/// Read-only, by the owner's decision of 2026-09-17 (<c>info.md</c>): it deletes nothing, because
/// what to do with such a row is an operator's call. Outside the pipeline for the reason
/// <see cref="IRetentionEnforcer"/> is: it spans every workspace and has no caller to authorise.
/// Audit events are not examined, because they outlive the project they describe on purpose.
/// </para>
/// </summary>
public interface IScopeIntegrityReport
{
    Task<IReadOnlyList<StrayScopeRows>> FindAsync(CancellationToken cancellationToken);
}

/// <summary>
/// How many rows of one table name one workspace and a project that is not live in it. Identifiers
/// and a count only, never content.
/// </summary>
public sealed record StrayScopeRows(string Table, Guid WorkspaceId, Guid ProjectId, int Rows);
