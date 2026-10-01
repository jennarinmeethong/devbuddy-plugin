import { Navigate, Link } from "react-router-dom";
import { useSession } from "../api/session";
import { Alert, BrandMark, Button, Panel } from "../components/ui";
import { LanguageSwitch } from "../components/LanguageSwitch";
import { t } from "../i18n";
import { Hint, TourButton } from "../components/Guide";
import { HINTS, TOURS } from "../guide/content";

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
    <div className="mx-auto max-w-2xl space-y-6 px-5 py-16">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <BrandMark />
          <h1 className="text-2xl font-bold">{t("Workspaces")}</h1>
        </div>
        <div className="flex items-center gap-3">
          <TourButton steps={TOURS.workspacePicker} />
          <LanguageSwitch />
          <Button onClick={() => void end()}>{t("Sign out")}</Button>
        </div>
      </div>

      <Panel title={t("Where you have access")} tour="workspace-picker" hint={<Hint topic={HINTS.role} />}>
        {distinct.length === 0 ? (
          <Alert>
            {t("Your account is not a member of any workspace. An administrator has to grant you one; there is nothing you can do from here.")}
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
