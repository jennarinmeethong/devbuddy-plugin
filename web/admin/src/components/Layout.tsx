import { NavLink, Outlet, useParams } from "react-router-dom";
import { grants, useSession, useWorkspace } from "../api/session";
import { Alert, Button } from "./ui";

/**
 * The frame every workspace screen sits in, and the navigation.
 *
 * The navigation is built from the permissions `/me` reported. A viewer is not offered membership
 * administration or the audit trail, because offering a person a page that will refuse them is a
 * worse experience than not offering it. The server refuses regardless, and the integration tests
 * are what prove that; nothing here is a control.
 */
export function Layout() {
  const { workspaceId } = useParams();
  const { user, end } = useSession();
  const access = useWorkspace(workspaceId);

  if (!access) {
    return (
      <div className="mx-auto max-w-2xl p-8">
        <Alert tone="error">
          You do not have access to this workspace, or it does not exist. Both look the same from
          here on purpose.
        </Alert>
      </div>
    );
  }

  const links = [
    { to: `/w/${workspaceId}`, label: "Projects", end: true, visible: true },
    {
      to: `/w/${workspaceId}/members`,
      label: "Members",
      end: false,
      visible: grants(access, "ManageAccess"),
    },
    {
      to: `/w/${workspaceId}/audit`,
      label: "Audit",
      end: false,
      visible: grants(access, "ReadAudit"),
    },
    {
      to: `/w/${workspaceId}/health`,
      label: "Health",
      end: false,
      visible: grants(access, "AdministerSystem"),
    },
    {
      to: `/w/${workspaceId}/plugin-access`,
      label: "Plugin access",
      end: false,

      // Everybody, including a viewer. A token carries its owner's permissions and no more, so
      // being able to mint one grants nothing that signing in does not.
      visible: grants(access, "ManageOwnCredentials"),
    },
  ].filter((link) => link.visible);

  return (
    <div className="min-h-full">
      <header className="border-b border-[var(--color-line)] bg-white">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-6 py-3">
          <div className="flex items-baseline gap-3">
            <span className="text-sm font-semibold">DevBuddy</span>
            <span className="text-sm text-[var(--color-muted)]">{access.name}</span>
            <span className="rounded bg-neutral-100 px-1.5 py-0.5 text-xs">{access.role}</span>
          </div>

          <div className="flex items-center gap-3">
            <span className="text-xs text-[var(--color-muted)]">{user?.email}</span>
            <Button onClick={() => void end()}>Sign out</Button>
          </div>
        </div>

        <nav aria-label="Workspace" className="mx-auto flex max-w-6xl gap-1 px-6">
          {links.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              className={({ isActive }) =>
                [
                  "border-b-2 px-3 py-2 text-sm",
                  isActive
                    ? "border-[var(--color-accent)] font-medium"
                    : "border-transparent text-[var(--color-muted)] hover:text-[var(--color-ink)]",
                ].join(" ")
              }
            >
              {link.label}
            </NavLink>
          ))}
        </nav>
      </header>

      <main className="mx-auto max-w-6xl space-y-6 p-6">
        <Outlet />
      </main>
    </div>
  );
}
