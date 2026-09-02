import { Navigate, Link } from "react-router-dom";
import { useSession } from "../api/session";
import { Alert, Button, Panel } from "../components/ui";

/**
 * The workspace picker.
 *
 * Skipped entirely when there is exactly one grant, which is the ordinary case for a self-hosted
 * installation: info.md asks for the simplest single-workspace experience without loosening any
 * project boundary, and a picker with one entry is a click that teaches nothing.
 */
export function Workspaces() {
  const { user, end } = useSession();
  const workspaces = user?.workspaces ?? [];

  const distinct = [...new Map(workspaces.map((access) => [access.workspaceId, access])).values()];

  if (distinct.length === 1) {
    return <Navigate to={`/w/${distinct[0]!.workspaceId}`} replace />;
  }

  return (
    <div className="mx-auto max-w-2xl space-y-4 p-8">
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Workspaces</h1>
        <Button onClick={() => void end()}>Sign out</Button>
      </div>

      <Panel title="Where you have access">
        {distinct.length === 0 ? (
          <Alert>
            Your account is not a member of any workspace. An administrator has to grant you one;
            there is nothing you can do from here.
          </Alert>
        ) : (
          <ul className="divide-y divide-[var(--color-line)]">
            {distinct.map((access) => (
              <li key={access.workspaceId} className="flex items-center justify-between py-2">
                <Link className="text-sm underline" to={`/w/${access.workspaceId}`}>
                  {access.name}
                </Link>
                <span className="text-xs text-[var(--color-muted)]">{access.role}</span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}
