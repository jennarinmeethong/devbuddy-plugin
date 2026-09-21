using DevBuddy.Domain.Common;

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
/// <see cref="FindAsync"/> is read-only, by the owner's decision of 2026-09-17 (<c>info.md</c>):
/// what to do with such a row is an operator's call. <see cref="PurgeAsync"/> is that call, added
/// in Phase 13 (D2) and never run by anything but an operator asking for it. Both are outside the
/// pipeline for the reason <see cref="IRetentionEnforcer"/> is: they span every workspace. Audit
/// events are not examined, because they outlive the project they describe on purpose.
/// </para>
/// </summary>
public interface IScopeIntegrityReport
{
    Task<IReadOnlyList<StrayScopeRows>> FindAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every stray row the report finds, and evidence bytes with their rows, the way
    /// <c>delete_project</c> deletes a project's content. Refused, with nothing changed, unless:
    /// <list type="bullet">
    /// <item><paramref name="expectedRows"/> equals the report's total at the moment of deleting,
    /// so an operator deletes exactly what they were shown; and</item>
    /// <item><paramref name="actor"/> administers, workspace-wide, every workspace a stray row
    /// belongs to, because deleting a workspace's rows is theirs to decide.</item>
    /// </list>
    /// One audit entry is written per workspace and project purged, as the actor, on the internal
    /// channel.
    /// </summary>
    Task<StrayScopePurge> PurgeAsync(UserId actor, int expectedRows, CancellationToken cancellationToken);
}

public enum StrayScopePurgeOutcome
{
    Purged = 1,
    NothingToPurge = 2,
    CountChanged = 3,
    NotAnAdministrator = 4,
}

/// <summary>What a purge did, and why it refused if it did.</summary>
public sealed record StrayScopePurge(
    StrayScopePurgeOutcome Outcome, string Message, IReadOnlyList<StrayScopeRows> Removed);

/// <summary>
/// How many rows of one table name one workspace and a project that is not live in it. Identifiers
/// and a count only, never content.
/// </summary>
public sealed record StrayScopeRows(string Table, Guid WorkspaceId, Guid ProjectId, int Rows);
