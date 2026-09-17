import { Api } from "../support/api";
import { unique } from "../support/env";
import { expect, projectPath, signIn, test } from "../support/fixtures";
import { invite } from "../support/people";

/**
 * What each role is offered, and — the part that matters — what the server refuses whatever the
 * page offered. Hiding a screen is a courtesy; these tests reach past it on purpose.
 */

const workspaceLinks = ["Projects", "Members", "Teams", "Workspaces", "Audit", "Health", "Plugin access"];
const projectLinks = ["Work items", "Knowledge records", "Search", "Analysis", "Evidence", "Maintenance"];

const offered = {
  Viewer: {
    workspace: ["Projects", "Plugin access"],
    project: ["Work items", "Knowledge records", "Search", "Evidence"],
  },
  Contributor: {
    workspace: ["Projects", "Plugin access"],
    project: ["Work items", "Knowledge records", "Search", "Analysis", "Evidence"],
  },
  Reviewer: {
    workspace: ["Projects", "Plugin access"],
    project: ["Work items", "Knowledge records", "Search", "Analysis", "Evidence"],
  },
  Administrator: { workspace: workspaceLinks, project: projectLinks },
} as const;

for (const role of ["Viewer", "Contributor", "Reviewer", "Administrator"] as const) {
  test(`${role === "Administrator" ? "an" : "a"} ${role.toLowerCase()} is offered exactly the screens their role can use`, async ({
    page,
    people,
    project,
  }) => {
    const person = {
      Viewer: people.viewer,
      Contributor: people.contributor,
      Reviewer: people.reviewer,
      Administrator: people.admin,
    }[role];

    await signIn(page, person, projectPath(project));

    const workspaceNav = page.getByRole("navigation", { name: "Workspace" });
    await expect(workspaceNav.getByRole("link")).toHaveText([...offered[role].workspace]);

    const projectNav = page.getByRole("navigation", { name: "Project" });
    await expect(projectNav.getByRole("link")).toHaveText([...offered[role].project]);

    const canRegister = role !== "Viewer";
    await expect(page.getByRole("heading", { name: "Register work" })).toHaveCount(canRegister ? 1 : 0);

    await page.getByRole("navigation", { name: "Workspace" }).getByRole("link", { name: "Projects" }).click();
    await expect(page.getByRole("link", { name: project.name })).toBeVisible();
    await expect(page.getByRole("heading", { name: "New project" })).toHaveCount(role === "Administrator" ? 1 : 0);
  });
}

test("a viewer who types an administration address is refused by the server, not just by the menu", async ({
  page,
  people,
}) => {
  await signIn(page, people.viewer, `/w/${people.workspaceId}/members`);

  await expect(page.getByRole("alert")).toContainText("Refused");
  await expect(page.getByRole("table")).toHaveCount(0);

  await page.goto(`/w/${people.workspaceId}/health`);
  await expect(page.getByRole("alert")).toContainText("Refused");
});

test("the API refuses every step a role does not carry", async ({ people, project, workItem }) => {
  const viewer = await Api.signIn(people.viewer.email, people.viewer.password);
  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const scope = { workspaceId: project.workspaceId, projectId: project.projectId };

  try {
    const refusals: [Api, string, unknown][] = [
      [viewer, "create_project", { workspaceId: people.workspaceId, name: unique("Viewer project") }],
      [viewer, "create_work_item", { scope, key: "NOPE-1", type: "Develop", title: "No", goal: "No" }],
      [viewer, "list_memberships", { workspaceId: people.workspaceId }],
      [viewer, "read_audit_history", { scope, occurredFrom: new Date(0).toISOString(), occurredUntil: new Date().toISOString() }],
      [viewer, "analyze_project", { scope }],
      [contributor, "enable_project_ai_access", { scope }],
      [contributor, "delete_project", { scope }],
      [contributor, "create_user_account", { workspaceId: people.workspaceId, email: "x@e2e.devbuddy.test", displayName: "X", role: "Administrator" }],
      [contributor, "backup_system", { workspaceId: people.workspaceId }],
      [contributor, "reindex", { scope }],
    ];

    for (const [api, name, args] of refusals) {
      const response = await api.call(name, args);
      expect(response.status(), `${name} should be refused`).toBe(403);
    }

    // And what the role does carry still works for the same people.
    expect((await viewer.call("get_work_item", { scope, workItemId: workItem.workItemId })).status()).toBe(200);
    expect((await contributor.call("analyze_work_items", { scope })).status()).toBe(200);
  } finally {
    await viewer.dispose();
    await contributor.dispose();
  }
});

test("a member of one workspace cannot reach another workspace, by page or by API", async ({
  page,
  admin,
  people,
}) => {
  // Somebody else's tenant: a second administrator, who stands up a workspace of their own.
  const owner = await invite(admin, people.workspaceId, "Administrator");
  const ownerApi = await Api.signIn(owner.email, owner.password);
  const viewer = await Api.signIn(people.viewer.email, people.viewer.password);

  try {
    const other = await ownerApi.invoke("create_workspace", {
      sponsorWorkspaceId: people.workspaceId,
      name: unique("Other tenant"),
      firstProjectName: "Their project",
    });
    const theirs = { workspaceId: other.workspaceId, projectId: other.projectId! };

    for (const [name, args] of [
      ["list_projects", { workspaceId: theirs.workspaceId }],
      ["list_work_items", { scope: theirs }],
      ["list_records", { scope: theirs }],
      ["search_knowledge", { scope: theirs, queryText: "anything" }],
      ["list_evidence", { scope: theirs }],
    ] as const) {
      const response = await viewer.call(name, args);
      expect([403, 404], `${name} across tenants`).toContain(response.status());
    }

    // A project identifier from the other tenant, presented inside the viewer's own workspace,
    // reads nothing of theirs: the workspace filter holds even where authorization lets it through.
    await ownerApi.invoke("create_work_item", {
      scope: theirs,
      key: "THEIRS-1",
      type: "Develop",
      title: "Their private work",
      goal: "Nobody outside this tenant reads it.",
    });
    const smuggled = { workspaceId: people.workspaceId, projectId: theirs.projectId };
    const listed = await viewer.call("list_work_items", { scope: smuggled });
    expect(await listed.text()).not.toContain("Their private work");
    const searched = await viewer.call("search_knowledge", { scope: smuggled, queryText: "private" });
    expect(await searched.text()).not.toContain("Their private work");

    // Signed in at home first: the refusal page has no header, so there is no Sign out to wait for.
    await signIn(page, people.viewer, "/");
    await page.goto(`/w/${theirs.workspaceId}`);
    await expect(page.getByRole("alert")).toContainText("You do not have access to this workspace");
  } finally {
    await ownerApi.dispose();
    await viewer.dispose();
  }
});

/**
 * KNOWN DEFECT, found by this suite on 2026-09-17. Authorization checks the caller's membership in
 * the workspace a request names, and never that the project belongs to that workspace — or exists.
 * Nothing leaks (the test above), but writes are accepted: a work item is stored in the caller's
 * workspace against another tenant's project identifier, or against one that exists nowhere.
 * `test.fail` keeps the suite green while the defect stands and turns red once it is fixed; remove
 * the marker then.
 */
test("a project identifier that is not in the named workspace is refused", async ({ admin, people }) => {
  test.fail(true, "Known defect: project membership of the workspace is not checked (2026-09-17).");

  const owner = await invite(admin, people.workspaceId, "Administrator");
  const ownerApi = await Api.signIn(owner.email, owner.password);

  try {
    const other = await ownerApi.invoke("create_workspace", {
      sponsorWorkspaceId: people.workspaceId,
      name: unique("Foreign tenant"),
      firstProjectName: "Foreign project",
    });

    for (const projectId of [other.projectId!, "11111111-1111-1111-1111-111111111111"]) {
      const scope = { workspaceId: people.workspaceId, projectId };

      const listed = await admin.call("list_work_items", { scope });
      expect([403, 404], `list_work_items for ${projectId}`).toContain(listed.status());

      const written = await admin.call("create_work_item", {
        scope,
        key: `STRAY-${projectId.slice(0, 4)}`,
        type: "Develop",
        title: "Written against a project this workspace does not have",
        goal: "Should be refused.",
      });
      expect([403, 404], `create_work_item for ${projectId}`).toContain(written.status());
    }
  } finally {
    await ownerApi.dispose();
  }
});

test("a grant scoped to one project reaches that project and no other", async ({ admin, people, project }) => {
  const elsewhere = await admin.invoke("create_project", {
    workspaceId: people.workspaceId,
    name: unique("Out of scope"),
  });

  const person = await invite(admin, people.workspaceId, "Contributor", { scopedToProject: project.projectId });
  const api = await Api.signIn(person.email, person.password);

  try {
    const inside = await api.call("list_work_items", {
      scope: { workspaceId: project.workspaceId, projectId: project.projectId },
    });
    expect(inside.status()).toBe(200);

    const outside = await api.call("list_work_items", {
      scope: { workspaceId: people.workspaceId, projectId: elsewhere.projectId },
    });
    expect([403, 404]).toContain(outside.status());
  } finally {
    await api.dispose();
  }
});
