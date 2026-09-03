using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Long-lived, revocable credentials for a process rather than a person.
/// <para>
/// A locally launched plugin cannot sign in: there is nobody at a keyboard when Claude Code or
/// Codex starts an MCP server over stdio, and a fifteen-minute access token in a configuration
/// file would stop working before the first question. A machine token is what goes in that file.
/// </para>
/// <para>
/// It is not a new authority. Resolving one produces a user identifier, and everything after that
/// is the same authorization the same person gets in the web UI — the same memberships, the same
/// roles, the same per-project AI policy. What it adds is a way to prove who you are without a
/// password, and a way to stop that proof working without changing one.
/// </para>
/// <para>
/// Deliberately not stored in the refresh-token table. That table's invariant is that a token is
/// used exactly once, which is what makes reuse detection mean theft; a row in it that is meant to
/// be presented every day would quietly undo that.
/// </para>
/// </summary>
public interface IMachineTokenService
{
    /// <summary>
    /// Mints a token for one person. The value is returned once and stored only as a hash.
    /// </summary>
    Task<MachineTokenIssued> IssueAsync(
        UserId userId, string name, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>
    /// Who this token belongs to, or null when it is unknown, revoked, or expired.
    /// <para>
    /// Null is the answer for all three on purpose. A caller holding a token that does not work
    /// has no business learning which of those it is.
    /// </para>
    /// </summary>
    Task<UserId?> ResolveAsync(string token, CancellationToken cancellationToken);

    Task<IReadOnlyList<MachineTokenSummary>> ListAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes one token, and only if it belongs to this person. Returns false when it does not,
    /// which is the same answer a token that never existed gets.
    /// </summary>
    Task<bool> RevokeAsync(UserId userId, MachineTokenId id, CancellationToken cancellationToken);
}

/// <summary>A freshly minted token. The value appears here and nowhere else, ever again.</summary>
public sealed record MachineTokenIssued(MachineTokenId Id, string Token, DateTimeOffset ExpiresAt);

public sealed record MachineTokenSummary(
    MachineTokenId Id,
    string Name,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive);
