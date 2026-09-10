using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Security;

/// <summary>
/// The limit a long-lived credential puts on the caller who presented it.
/// <para>
/// A signed-in person has none: a session token proves who they are, and where they may act is
/// decided by their memberships, which is the whole of the answer for a human at a keyboard.
/// A machine token is different. It sits in a configuration file on a laptop for ninety days,
/// beside whichever checkout happens to be open, and its owner may well be a member of several
/// workspaces. Binding it to one of them means a file that leaks, or a session launched from the
/// wrong root, reaches one workspace rather than every workspace its owner belongs to.
/// </para>
/// <para>
/// This is a ceiling and never a grant. Holding a credential scoped to a workspace authorises
/// nothing there; the memberships, roles, and per-project AI policy are checked afterwards
/// exactly as they always were (SB-11). What the scope can do is take permissions away.
/// </para>
/// <para>
/// Deliberately no client identifier. Claude and Codex are processes that hold a credential, not
/// identity providers, and a token that recorded which of them presented it would invite the
/// mistake of treating that record as authentication. Minting one token per assistant is
/// available to anybody who wants separate revocation, and means nothing to authorization.
/// </para>
/// </summary>
public sealed record CredentialScope(MachineTokenId TokenId, WorkspaceId WorkspaceId);
