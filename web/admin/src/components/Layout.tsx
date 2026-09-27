import { useEffect, useState } from "react";
import { Link, NavLink, Outlet, useLocation, useParams } from "react-router-dom";
import { grants, useSession, useWorkspace } from "../api/session";
import { Alert, BrandMark, Button, Icon, type IconName } from "./ui";
import { m, t } from "../i18n";
import { roleLabel } from "./labels";
import { LanguageSwitch } from "./LanguageSwitch";

/**
 * The frame every workspace screen sits in, and the navigation.
 *
 * The navigation is built from the permissions `/me` reported. A viewer is not offered membership
 * administration or the audit trail, because offering a person a page that will refuse them is a
 * worse experience than not offering it. The server refuses regardless, and the integration tests
 * are what prove that; nothing here is a control.
 *
 * The navigation is a sidebar, from the Ocean Mist Light demo in `demo/`. Below the `lg` breakpoint
 * it is a drawer behind the menu button, and it is `invisible` while closed, so its links cannot be
 * reached by keyboard or read out while nobody can see them. The `header` stays the page's banner:
 * it carries the workspace, the role, the address and Sign out, which is where the e2e suite looks.
 */
export function Layout() {
  const { workspaceId } = useParams();
  const { user, end } = useSession();
  const access = useWorkspace(workspaceId);
  const { pathname } = useLocation();
  const [open, setOpen] = useState(false);

  // Following a link in the drawer closes it.
  useEffect(() => setOpen(false), [pathname]);

  if (!access) {
    return (
      <div className="mx-auto max-w-2xl p-8">
        <Alert tone="error">
          {t("You do not have access to this workspace, or it does not exist. Both look the same from here on purpose.")}
        </Alert>
      </div>
    );
  }

  const links = [
    { to: `/w/${workspaceId}`, label: m("Projects"), icon: "projects" as IconName, end: true, visible: true },
    {
      to: `/w/${workspaceId}/members`,
      label: m("Members"),
      icon: "members" as IconName,
      end: false,
      visible: grants(access, "ManageAccess"),
    },
    {
      to: `/w/${workspaceId}/teams`,
      label: m("Teams"),
      icon: "teams" as IconName,
      end: false,
      visible: grants(access, "ManageTeams"),
    },
    {
      to: `/w/${workspaceId}/workspaces`,
      label: m("Workspaces"),
      icon: "workspaces" as IconName,
      end: false,

      // Held on this workspace, and it is what lets somebody stand up another one sponsored by it.
      // There is no installation-wide role behind this and the screen says so.
      visible: grants(access, "ProvisionWorkspace"),
    },
    {
      to: `/w/${workspaceId}/audit`,
      label: m("Audit"),
      icon: "audit" as IconName,
      end: false,
      visible: grants(access, "ReadAudit"),
    },
    {
      to: `/w/${workspaceId}/health`,
      label: m("Health"),
      icon: "health" as IconName,
      end: false,
      visible: grants(access, "AdministerSystem"),
    },
    {
      to: `/w/${workspaceId}/plugin-access`,
      label: m("Plugin access"),
      icon: "key" as IconName,
      end: false,

      // Everybody, including a viewer. A token carries its owner's permissions and no more, so
      // being able to mint one grants nothing that signing in does not.
      visible: grants(access, "ManageOwnCredentials"),
    },
  ].filter((link) => link.visible);

  // A project's screens are under the Projects entry, whose own link matches only the list.
  const inProject = pathname.startsWith(`/w/${workspaceId}/p/`);
  const lit = (link: (typeof links)[number], isActive: boolean) => isActive || (inProject && link.icon === "projects");
  const current = links.find((link) =>
    lit(link, link.end ? pathname === link.to : pathname.startsWith(link.to)),
  );

  return (
    <div className="min-h-full lg:grid lg:grid-cols-[17rem_minmax(0,1fr)]">
      <aside
        id="sidebar"
        className={[
          "fixed inset-y-0 left-0 z-30 flex w-72 flex-col border-r border-[var(--color-line)]",
          "bg-[var(--color-sidebar)] px-4 pt-6 pb-5 shadow-[12px_0_38px_rgba(13,59,102,0.08)] backdrop-blur-xl",
          "transition-transform duration-200 lg:sticky lg:top-0 lg:h-screen lg:w-auto lg:translate-x-0",
          open ? "translate-x-0" : "max-lg:invisible max-lg:-translate-x-full",
        ].join(" ")}
      >
        <div className="flex items-center justify-between gap-2 px-2 pb-7">
          <Link to="/" className="flex items-center gap-3 rounded-lg">
            <BrandMark />
            <span className="leading-tight">
              <strong lang="en" className="block text-sm font-semibold tracking-[0.17em]">DEVBUDDY</strong>
              <span className="mt-1 block text-[0.68rem] tracking-[0.18em] text-[var(--color-muted)]">
                {t("ADMINISTRATION")}
              </span>
            </span>
          </Link>
          <button
            type="button"
            aria-label={t("Close menu")}
            onClick={() => setOpen(false)}
            className="grid size-9 place-items-center rounded-lg border border-[var(--color-line)] bg-[var(--color-soft)] hover:bg-[var(--color-hover)] lg:hidden"
          >
            <Icon name="close" />
          </button>
        </div>

        <p className="mx-3 mb-2 text-xs font-bold tracking-widest text-[var(--color-muted)] uppercase">{t("Workspace")}</p>
        <nav aria-label={t("Workspace")} className="flex-1 space-y-1 overflow-y-auto">
          {links.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              className={({ isActive }) =>
                [
                  "flex min-h-11 items-center gap-3 rounded-lg border px-3 text-sm transition",
                  lit(link, isActive)
                    ? [
                        "border-[var(--color-line)] bg-linear-to-r from-[rgba(13,59,102,0.19)] to-[rgba(13,59,102,0.07)]",
                        "font-medium text-[var(--color-ink)]",
                        "shadow-[inset_3px_0_0_var(--color-accent),var(--shadow-small)]",
                      ].join(" ")
                    : [
                        "border-transparent text-[var(--color-muted)]",
                        "hover:border-[var(--color-line)] hover:bg-[var(--color-soft)] hover:text-[var(--color-ink)]",
                      ].join(" "),
                ].join(" ")
              }
            >
              {({ isActive }) => (
                <>
                  <Icon name={link.icon} className={lit(link, isActive) ? "text-[var(--color-accent-bright)]" : "opacity-80"} />
                  {t(link.label)}
                </>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="border-t border-[var(--color-line)] px-2 pt-4 text-xs text-[var(--color-muted)]">
          <p className="flex items-center gap-2 text-sm text-[var(--color-ink)]">
            <span
              aria-hidden="true"
              className="size-2 shrink-0 rounded-full bg-[var(--color-success)] ring-4 ring-[var(--color-success-soft)]"
            />
            <span className="truncate">{access.name}</span>
          </p>
          <p className="mt-1">{t("DevBuddy administration")}</p>
        </div>
      </aside>

      {open ? (
        <button
          type="button"
          aria-label={t("Close menu")}
          tabIndex={-1}
          onClick={() => setOpen(false)}
          className="fixed inset-0 z-20 bg-black/40 lg:hidden"
        />
      ) : null}

      <div className="min-w-0">
        <header className="sticky top-0 z-10 border-b border-[var(--color-line)] bg-[var(--color-topbar)] backdrop-blur-xl">
          <div className="flex min-h-16 items-center justify-between gap-4 px-5 py-2 lg:px-8">
            <div className="flex min-w-0 items-center gap-3">
              <button
                type="button"
                aria-label={t("Open menu")}
                aria-controls="sidebar"
                aria-expanded={open}
                onClick={() => setOpen(true)}
                className="grid size-10 shrink-0 place-items-center rounded-lg border border-[var(--color-line)] bg-[var(--color-soft)] shadow-small hover:bg-[var(--color-hover)] lg:hidden"
              >
                <Icon name="menu" />
              </button>
              <div className="flex min-w-0 items-center gap-2 text-sm text-[var(--color-muted)]">
                <span className="truncate font-medium text-[var(--color-ink)]">{access.name}</span>
                {current ? (
                  <>
                    <span aria-hidden="true" className="text-[var(--color-line-strong)] max-sm:hidden">
                      /
                    </span>
                    <span className="truncate max-sm:hidden">{t(current.label)}</span>
                  </>
                ) : null}
              </div>
            </div>

            <div className="flex items-center gap-3">
              <LanguageSwitch />
              <span className="rounded-md border border-[var(--color-line)] bg-[var(--color-accent-soft)] px-2 py-0.5 text-xs font-medium text-[var(--color-accent)]">
                {t(roleLabel(access.role))}
              </span>
              <span className="text-xs text-[var(--color-muted)] max-md:sr-only">{user?.email}</span>
              <Button onClick={() => void end()}>{t("Sign out")}</Button>
            </div>
          </div>
        </header>

        <main className="mx-auto max-w-6xl space-y-6 px-5 py-8 lg:px-8">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
