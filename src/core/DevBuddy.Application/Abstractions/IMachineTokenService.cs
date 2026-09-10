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
/// It is not a new authority. Resolving one produces a person and one workspace, and everything
/// after that is the same authorization the same person gets in the web UI — the same
/// memberships, the same roles, the same per-project AI policy. What it adds is a way to prove
/// who you are without a password, a way to stop that proof working without changing one, and a
/// ceiling on where it works at all.
/// </para>
/// <para>
/// A token is bound to a user <em>and</em> a workspace. Without the second half one credential
/// sitting in a global plugin configuration reached every workspace its owner belonged to, from
/// whichever checkout the assistant happened to be started in, and nothing on the server could
/// tell that this was not intended. With it, a token minted in one workspace is refused in every
/// other one, and a person working across several mints one per workspace.
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
    /// Mints a token for one person in one workspace. The value is returned once and stored only
    /// as a hash.
    /// <para>
    /// The workspace is the caller's own request scope, which the pipeline has already authorised
    /// by the time this runs, so a token can never be minted for a workspace its owner has no
    /// live grant in.
    /// </para>
    /// </summary>
    Task<MachineTokenIssued> IssueAsync(
        UserId userId,
        WorkspaceId workspaceId,
        string name,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    /// <summary>
    /// Who this token belongs to and where it works, or null when it is unknown, revoked,
    /// expired, or carries no workspace at all.
    /// <para>
    /// Null is the answer for all four on purpose. A caller holding a token that does not work
    /// has no business learning which of them it is.
    /// </para>
    /// <para>
    /// The last case is the tokens issued before this was workspace-scoped. They are refused
    /// rather than adopted into a workspace, because no adoption could be made from the row: a
    /// token whose owner belongs to three workspaces has no correct one to be moved into, and
    /// guessing would hand a credential authority in a workspace nobody chose to give it
    /// (fail closed).
    /// </para>
    /// </summary>
    Task<MachineTokenIdentity?> ResolveAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// The caller's own tokens for one workspace, plus any of their own unscoped tokens left over
    /// from before scoping, which are listed as needing replacement so they can be cleared out.
    /// Another workspace's tokens never appear.
    /// </summary>
    Task<IReadOnlyList<MachineTokenSummary>> ListAsync(
        UserId userId, WorkspaceId workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes one token, and only if it belongs to this person in this workspace. Returns false
    /// when it does not, which is the same answer a token that never existed gets — so a caller
    /// cannot learn that a token exists in a workspace they are asking from the outside about.
    /// <para>
    /// The caller's own unscoped legacy tokens are revocable from any workspace they are in.
    /// They work nowhere, they belong to nobody's workspace, and leaving them unrevocable would
    /// mean rows that can never be tidied away.
    /// </para>
    /// </summary>
    Task<bool> RevokeAsync(
        UserId userId, WorkspaceId workspaceId, MachineTokenId id, CancellationToken cancellationToken);
}

/// <summary>A freshly minted token. The value appears here and nowhere else, ever again.</summary>
public sealed record MachineTokenIssued(
    MachineTokenId Id, WorkspaceId WorkspaceId, string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// What a presented token proves: one person, one workspace, and which credential said so.
/// <para>
/// Returned instead of a bare user identifier because the workspace is the point. A host that
/// only learned the user would have to look the scope up again, and a host that forgot to would
/// silently give the token back the reach it was meant to lose.
/// </para>
/// <para>
/// Nothing here identifies the assistant holding it. A DevBuddy token authenticates a DevBuddy
/// user; it neither carries nor verifies a Claude or an OpenAI account.
/// </para>
/// </summary>
public sealed record MachineTokenIdentity(
    MachineTokenId TokenId,
    UserId UserId,
    WorkspaceId WorkspaceId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// One token as its owner sees it. Never the value: the store keeps a hash, so there is nothing
/// to show and nothing to leak through a listing.
/// </summary>
/// <param name="NeedsReplacement">
/// True for a token issued before tokens were bound to a workspace. It is refused on every call
/// and cannot be repaired — the stored value is a hash — so it is listed only so its owner can
/// see why their plugin stopped working, and revoke it.
/// </param>
public sealed record MachineTokenSummary(
    MachineTokenId Id,
    string Name,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive,
    bool NeedsReplacement = false);
