import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import type { PermissionName } from "./operations";
import type { SignedInUser, WorkspaceAccess } from "./client";
import { describeSignedInUser, forgetTokens, hasSession, refreshSession, signOut } from "./client";

/**
 * Who is signed in, and what the server says they may do.
 *
 * The permissions here drive navigation: a screen the caller could not use is not offered. That
 * is a courtesy and never a control. Every one of those operations is authorised again on the
 * server, and the integration tests prove a viewer is refused whether or not this file ever
 * hid the button (SB-16).
 */

interface Session {
  user: SignedInUser | null;
  /**
   * True only until the first answer about who is signed in has arrived. A later `reload` leaves
   * it alone and swaps `user` when the new answer lands: App shows "Loading…" in place of every
   * route while this is true, so a refresh that set it would unmount the screen that asked for it,
   * and whatever that screen was showing — a confirmation, a half-filled form — with it.
   */
  loading: boolean;
  reload: () => Promise<void>;
  end: () => Promise<void>;
}

const SessionContext = createContext<Session | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SignedInUser | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    if (!hasSession()) {
      setUser(null);
      setLoading(false);
      return;
    }

    try {
      setUser(await describeSignedInUser());
    } catch {
      // A rejected /me means the token is gone, the account was disabled, or its access was
      // revoked. All three are "signed out" from here; the server decided, not this page.
      forgetTokens();
      setUser(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    // On a reload there is a refresh token in storage but no access token in memory, so the
    // session is re-established before anything asks for data.
    void (async () => {
      if (hasSession()) {
        await refreshSession();
      }

      await reload();
    })();
  }, [reload]);

  const end = useCallback(async () => {
    await signOut();
    setUser(null);
  }, []);

  const value = useMemo<Session>(() => ({ user, loading, reload, end }), [user, loading, reload, end]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): Session {
  const session = useContext(SessionContext);

  if (!session) {
    throw new Error("useSession was called outside a SessionProvider.");
  }

  return session;
}

/**
 * What the caller may do in one workspace, or in one project of it, across every grant they hold
 * there; undefined when they hold none.
 *
 * A person can hold several grants in one workspace — a workspace-wide Viewer and a Reviewer on one
 * project, say. This used to return whichever grant came first, so the menus followed the order the
 * grants were made in (Phase 13, A5). The server authorises every request itself; this only decides
 * what is offered.
 */
export function useWorkspace(workspaceId: string | undefined, projectId?: string): WorkspaceAccess | undefined {
  const { user } = useSession();

  return mergeAccess(user?.workspaces.filter((workspace) => workspace.workspaceId === workspaceId) ?? [], projectId);
}

/**
 * Merges the grants one person holds in one workspace.
 *
 * - For a project: the workspace-wide grants and the grants on that project.
 * - For the workspace itself: the workspace-wide grants; a person with only project grants is
 *   offered the union of those, as before, and the server refuses what they do not cover.
 */
export function mergeAccess(grantsHere: WorkspaceAccess[], projectId?: string): WorkspaceAccess | undefined {
  if (grantsHere.length === 0) {
    return undefined;
  }

  const workspaceWide = grantsHere.filter((grant) => grant.scopedToProject === null);
  const relevant = projectId
    ? grantsHere.filter((grant) => grant.scopedToProject === null || grant.scopedToProject === projectId)
    : workspaceWide.length > 0
      ? workspaceWide
      : grantsHere;

  const first = relevant[0] ?? grantsHere[0];

  if (!first) {
    return undefined;
  }

  const permissions = [...new Set(relevant.flatMap((grant) => grant.permissions))];

  return {
    ...first,
    scopedToProject: workspaceWide.length > 0 ? null : first.scopedToProject,
    permissions,
  };
}

/** Whether the caller's grant on this workspace carries a permission. */
export function grants(access: WorkspaceAccess | undefined, permission: PermissionName): boolean {
  return access?.permissions.includes(permission) ?? false;
}
