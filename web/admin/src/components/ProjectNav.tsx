import { NavLink, useParams } from "react-router-dom";
import { grants, useWorkspace } from "../api/session";

/**
 * The screens inside one project, and the heading above them.
 *
 * Built from the permissions `/me` reported, like the workspace navigation: a person is not
 * offered a screen the server would refuse them. The server refuses regardless.
 */
export function ProjectNav({ title }: { title: string }) {
  const { workspaceId, projectId } = useParams();
  const access = useWorkspace(workspaceId);
  const base = `/w/${workspaceId}/p/${projectId}`;

  const links = [
    { to: base, label: "Work items", end: true, visible: true },
    { to: `${base}/records`, label: "Knowledge records", end: false, visible: true },
    { to: `${base}/search`, label: "Search", end: false, visible: grants(access, "ReadKnowledge") },
    { to: `${base}/analysis`, label: "Analysis", end: false, visible: grants(access, "AnalyzeProject") },
    { to: `${base}/evidence`, label: "Evidence", end: false, visible: true },
    {
      to: `${base}/maintenance`,
      label: "Maintenance",
      end: false,
      visible:
        grants(access, "ManageIndex") || grants(access, "ScanContent") || grants(access, "AdministerSystem"),
    },
  ].filter((link) => link.visible);

  return (
    <div className="space-y-2">
      <h1 className="text-lg font-semibold">{title}</h1>
      <nav aria-label="Project" className="flex flex-wrap gap-3 text-sm">
        {links.map((link) => (
          <NavLink
            key={link.to}
            to={link.to}
            end={link.end}
            className={({ isActive }) =>
              isActive ? "font-medium underline" : "text-[var(--color-muted)] underline hover:text-[var(--color-ink)]"
            }
          >
            {link.label}
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
