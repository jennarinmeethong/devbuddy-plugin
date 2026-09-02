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

    setLoading(true);

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

/** The caller's grant on one workspace, or undefined when they hold none. */
export function useWorkspace(workspaceId: string | undefined): WorkspaceAccess | undefined {
  const { user } = useSession();

  return user?.workspaces.find((workspace) => workspace.workspaceId === workspaceId);
}

/** Whether the caller's grant on this workspace carries a permission. */
export function grants(access: WorkspaceAccess | undefined, permission: PermissionName): boolean {
  return access?.permissions.includes(permission) ?? false;
}
