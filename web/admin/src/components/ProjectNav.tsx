import { NavLink, useParams } from "react-router-dom";
import { grants, useWorkspace } from "../api/session";
import { m, t } from "../i18n";

/**
 * The screens inside one project, and the heading above them.
 *
 * Built from the permissions `/me` reported, like the workspace navigation: a person is not
 * offered a screen the server would refuse them. The server refuses regardless.
 */
export function ProjectNav({ title }: { title: string }) {
  const { workspaceId, projectId } = useParams();
  const access = useWorkspace(workspaceId, projectId);
  const base = `/w/${workspaceId}/p/${projectId}`;

  const links = [
    { to: base, label: m("Work items"), end: true, visible: true },
    { to: `${base}/records`, label: m("Knowledge records"), end: false, visible: true },
    { to: `${base}/search`, label: m("Search"), end: false, visible: grants(access, "ReadKnowledge") },
    { to: `${base}/analysis`, label: m("Analysis"), end: false, visible: grants(access, "AnalyzeProject") },
    { to: `${base}/evidence`, label: m("Evidence"), end: false, visible: true },
    {
      to: `${base}/maintenance`,
      label: m("Maintenance"),
      end: false,
      visible:
        grants(access, "ManageIndex") || grants(access, "ScanContent") || grants(access, "AdministerSystem"),
    },
  ].filter((link) => link.visible);

  return (
    <div className="space-y-4">
      <div>
        <p className="mb-1 text-xs font-bold tracking-[0.16em] text-[var(--color-accent-bright)] uppercase">{t("Project")}</p>
        <h1 className="text-2xl font-bold">{title}</h1>
      </div>
      <nav
        aria-label={t("Project")}
        data-tour="project-nav"
        className="flex flex-wrap gap-1 rounded-xl border border-[var(--color-line)] bg-[var(--color-surface)] p-1 text-sm shadow-small"
      >
        {links.map((link) => (
          <NavLink
            key={link.to}
            to={link.to}
            end={link.end}
            className={({ isActive }) =>
              [
                "rounded-lg px-3 py-1.5 transition",
                isActive
                  ? "bg-[var(--color-accent)] font-medium text-[var(--color-on-accent)] shadow-small"
                  : "text-[var(--color-muted)] hover:bg-[var(--color-soft)] hover:text-[var(--color-ink)]",
              ].join(" ")
            }
          >
            {t(link.label)}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
