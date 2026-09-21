import { randomBytes } from "node:crypto";
import { anonymous, Api } from "../support/api";
import { password, unique } from "../support/env";
import { expect, openAs, signIn, test } from "../support/fixtures";
import { invite } from "../support/people";

test.describe("members", () => {
  test("an administrator invites somebody, who sets a password from the token and signs in", async ({
    page,
    browser,
    people,
  }) => {
    const email = `invited.${randomBytes(4).toString("hex")}@e2e.devbuddy.test`;

    await signIn(page, people.admin, `/w/${people.workspaceId}/members`);
    const invitePanel = page.locator("section", { has: page.getByRole("heading", { name: "Add somebody" }) });
    await invitePanel.getByRole("textbox", { name: /^Email/ }).fill(email);
    await invitePanel.getByRole("textbox", { name: /^Display name/ }).fill("Invited Person");
    await invitePanel.getByRole("combobox", { name: /^Role/ }).selectOption("Reviewer");
    await invitePanel.getByRole("button", { name: "Create account" }).click();

    await expect(invitePanel.getByRole("status")).toContainText("shown once");
    const token = (await invitePanel.locator("code").innerText()).trim();
    expect(token.length).toBeGreaterThan(20);

    const chosen = password();
    const newcomer = await browser.newContext();
    const tab = await newcomer.newPage();
    await tab.goto(`/set-password?token=${encodeURIComponent(token)}`);
    await tab.getByLabel(/^New password/).fill(chosen);
    await tab.getByLabel(/^Confirm password/).fill(chosen);
    await tab.getByRole("button", { name: "Set password" }).click();
    await expect(tab.getByRole("status")).toContainText("Your password is set");

    await tab.getByRole("link", { name: "Sign in" }).click();
    await tab.getByRole("textbox", { name: /^Email/ }).fill(email);
    await tab.getByLabel(/^Password/).fill(chosen);
    await tab.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(tab.getByRole("banner")).toContainText("Reviewer");
    await newcomer.close();
  });

  test("changing a role revokes the old grant and makes a new one", async ({ page, admin, people }) => {
    const person = await invite(admin, people.workspaceId, "Viewer");

    await signIn(page, people.admin, `/w/${people.workspaceId}/members`);
    const rows = page.getByRole("row", { name: new RegExp(person.userId) });
    await expect(rows).toHaveCount(1);

    const change = rows.getByRole("combobox", { name: `New role for ${person.userId}` });
    await expect(rows.getByRole("button", { name: "Change role" })).toBeDisabled();
    await change.selectOption("Contributor");
    await rows.getByRole("button", { name: "Change role" }).click();

    await expect(rows).toHaveCount(2);
    await expect(rows.filter({ hasText: "Revoked" }).getByRole("cell").nth(1)).toHaveText("Viewer");
    await expect(rows.filter({ hasText: "Active" }).getByRole("cell").nth(1)).toHaveText("Contributor");

    const api = await Api.signIn(person.email, person.password);
    try {
      const me = await api.me();
      expect(me.workspaces.map((grant) => grant.role)).toEqual(["Contributor"]);
    } finally {
      await api.dispose();
    }
  });

  test("the administrator's own grant offers no role change", async ({ page, people }) => {
    await signIn(page, people.admin, `/w/${people.workspaceId}/members`);
    const own = page.getByRole("row", { name: new RegExp(people.admin.userId) }).filter({ hasText: "Active" });
    await expect(own).toHaveCount(1);
    await expect(own.getByRole("button", { name: "Change role" })).toHaveCount(0);
  });

  test("a revoked person is locked out on their next request, not their next sign-in", async ({
    page,
    browser,
    admin,
    people,
  }) => {
    const person = await invite(admin, people.workspaceId, "Contributor");
    const theirs = await openAs(browser, person);
    await expect(theirs.getByRole("banner")).toContainText("Contributor");

    await signIn(page, people.admin, `/w/${people.workspaceId}/members`);
    const row = page.getByRole("row", { name: new RegExp(person.userId) });
    await row.getByRole("button", { name: "Revoke" }).click();
    await expect(row).toContainText("Revoked");

    // Same session, no new sign-in.
    await theirs.reload();
    await expect(theirs.getByRole("alert")).toContainText("You do not have access to this workspace");
    await theirs.goto("/");
    await expect(theirs.getByText("Your account is not a member of any workspace.")).toBeVisible();
    await theirs.context().close();
  });

  test("somebody already here is given a grant on one project", async ({ page, admin, people, project }) => {
    const person = await invite(admin, people.workspaceId, "Viewer");

    await signIn(page, people.admin, `/w/${people.workspaceId}/members`);
    const grant = page.locator("section", {
      has: page.getByRole("heading", { name: "Grant access to somebody already here" }),
    });
    await grant.getByRole("combobox", { name: /^Person/ }).selectOption(person.userId);
    await grant.getByRole("combobox", { name: /^Role/ }).selectOption("Reviewer");
    await grant.getByRole("combobox", { name: /^Where/ }).selectOption({ label: project.name });
    await grant.getByRole("button", { name: "Grant", exact: true }).click();
    await expect(grant.getByRole("status")).toHaveText("Granted Reviewer.");

    await expect(
      page.getByRole("row", { name: new RegExp(person.userId) }).filter({ hasText: `Project ${project.projectId}` }),
    ).toContainText("Reviewer");
  });

  test("an administrator issues a password reset, and the person signs in with the new password", async ({
    page,
    browser,
    admin,
    people,
  }) => {
    const person = await invite(admin, people.workspaceId, "Viewer");

    await signIn(page, people.admin, `/w/${people.workspaceId}/members`);
    const row = page.getByRole("row", { name: new RegExp(person.userId) });
    await row.getByRole("button", { name: "Reset password" }).click();
    await row.getByRole("button", { name: "Issue reset token" }).click();
    await expect(row.getByRole("status")).toContainText("shown once");
    const token = (await row.getByLabel("Reset token").innerText()).trim();
    expect(token.length).toBeGreaterThan(20);

    const chosen = password();
    const context = await browser.newContext();
    const tab = await context.newPage();
    await tab.goto(`/set-password?token=${encodeURIComponent(token)}`);
    await tab.getByLabel(/^New password/).fill(chosen);
    await tab.getByLabel(/^Confirm password/).fill(chosen);
    await tab.getByRole("button", { name: "Set password" }).click();
    await expect(tab.getByRole("status")).toContainText("Your password is set");
    await context.close();

    // The new password works, and the old one no longer does.
    const api = await Api.signIn(person.email, chosen);
    await api.dispose();
    await expect(Api.signIn(person.email, person.password)).rejects.toThrow();
  });

  test("a password reset is refused for somebody who also belongs to a workspace the administrator does not run", async ({
    admin,
    people,
  }) => {
    const person = await invite(admin, people.workspaceId, "Viewer");
    const owner = await invite(admin, people.workspaceId, "Administrator");
    const ownerApi = await Api.signIn(owner.email, owner.password);

    try {
      const other = await ownerApi.invoke("create_workspace", {
        sponsorWorkspaceId: people.workspaceId,
        name: unique("Reset elsewhere"),
        firstProjectName: "Theirs",
      });
      await ownerApi.invoke("grant_membership", {
        workspaceId: other.workspaceId,
        subjectUserId: person.userId,
        role: "Viewer",
      });

      const refused = await admin.call("issue_password_reset", {
        workspaceId: people.workspaceId,
        subjectUserId: person.userId,
      });
      expect(refused.status()).toBe(403);
      expect(await refused.text()).toContain("do not administer");

      // The owner administers both, so it is theirs to do.
      const allowed = await ownerApi.call("issue_password_reset", {
        workspaceId: people.workspaceId,
        subjectUserId: person.userId,
      });
      expect(allowed.status()).toBe(200);
    } finally {
      await ownerApi.dispose();
    }
  });
});

test.describe("teams", () => {
  test("a team is created, renamed, staffed and deleted", async ({ page, people }) => {
    const name = unique("Platform");
    const renamed = unique("Payments");

    await signIn(page, people.admin, `/w/${people.workspaceId}/teams`);

    await page.getByRole("textbox", { name: /^Name/ }).fill(name);
    await page.getByRole("button", { name: "Create", exact: true }).click();
    const row = () => page.getByRole("row", { name: new RegExp(`${name}|${renamed}`) }).first();
    await expect(row()).toBeVisible();

    await row().getByRole("button", { name: "Rename" }).click();
    await page.getByRole("textbox", { name: `New name for ${name}` }).fill(renamed);
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: renamed })).toBeVisible();
    await expect(page.getByRole("button", { name, exact: true })).toHaveCount(0);

    await page.getByRole("button", { name: renamed }).click();
    await expect(page.getByText("Nobody is on this team yet.")).toBeVisible();

    const picker = page.getByRole("combobox", { name: `Add somebody to ${renamed}` });
    await picker.selectOption({ label: `${people.contributor.userId} (Contributor)` });
    await page.getByRole("button", { name: "Add", exact: true }).click();

    const member = page.getByRole("listitem").filter({ hasText: people.contributor.userId });
    await expect(member).toBeVisible();

    // Somebody already on the team is no longer offered.
    await expect(picker.getByRole("option", { name: new RegExp(people.contributor.userId) })).toHaveCount(0);

    await member.getByRole("button", { name: "Remove" }).click();
    await expect(page.getByText("Nobody is on this team yet.")).toBeVisible();

    await page.getByRole("row", { name: new RegExp(renamed) }).first().getByRole("button", { name: "Delete" }).click();
    await expect(page.getByText("Delete this team?")).toBeVisible();
    await page.getByRole("button", { name: "Confirm" }).click();
    await expect(page.getByRole("button", { name: renamed })).toHaveCount(0);
  });
});

test.describe("workspaces", () => {
  test("an administrator stands up another workspace and becomes its administrator", async ({
    page,
    admin,
    people,
  }) => {
    // A person of the test's own, so the shared administrator keeps exactly one workspace.
    const founder = await invite(admin, people.workspaceId, "Administrator");
    const name = unique("Northwind");

    await signIn(page, founder, `/w/${people.workspaceId}/workspaces`);
    await expect(page.getByRole("row", { name: new RegExp(people.workspaceName) })).toContainText("You are here");

    await page.getByRole("textbox", { name: /^Name/ }).fill(name);
    await page.getByRole("textbox", { name: /^First project/ }).fill("Warehouse");
    await page.getByRole("button", { name: "Create workspace" }).click();

    // Opened from the list of reachable workspaces rather than from the confirmation; see the
    // known defect below.
    await page.getByRole("row", { name: new RegExp(name) }).getByRole("link", { name }).click();

    await expect(page.getByRole("banner")).toContainText(name);
    await expect(page.getByRole("banner")).toContainText("Administrator");
    await expect(page.getByRole("link", { name: "Warehouse" })).toBeVisible();

    // Two workspaces now, so home is the picker.
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Workspaces" })).toBeVisible();
    await expect(page.getByRole("link", { name })).toBeVisible();
    await expect(page.getByRole("link", { name: people.workspaceName })).toBeVisible();

    // Nobody was carried over: the sponsor's other members are not in the new workspace.
    await page.getByRole("link", { name }).click();
    await page.getByRole("navigation", { name: "Workspace" }).getByRole("link", { name: "Members" }).click();
    await expect(page.getByRole("row", { name: new RegExp(people.contributor.userId) })).toHaveCount(0);
    await expect(page.getByRole("row", { name: new RegExp(founder.userId) })).toHaveCount(1);
  });
});

/**
 * Found by this suite on 2026-09-17 and fixed the same day. Creating a workspace re-reads the
 * session, the session used to set `loading`, and App then replaced every route with "Loading…" —
 * so the screen unmounted and the confirmation, with its link to the new workspace, was never seen.
 * A re-read no longer sets `loading`; only the first load does.
 */
test("the confirmation after creating a workspace stays on screen", async ({ page, admin, people }) => {
  const founder = await invite(admin, people.workspaceId, "Administrator");
  const name = unique("Confirmed");

  await signIn(page, founder, `/w/${people.workspaceId}/workspaces`);
  await page.getByRole("textbox", { name: /^Name/ }).fill(name);
  await page.getByRole("button", { name: "Create workspace" }).click();

  const created = page.getByRole("status").filter({ hasText: "Created" });
  await expect(created).toContainText(`sponsored by ${people.workspaceName}`, { timeout: 5_000 });
  await expect(created.getByRole("link", { name: `open ${name}` })).toBeVisible();
});

test.describe("plugin access", () => {
  test("a viewer mints a token that is shown once, and revokes it", async ({ page, people }) => {
    const name = unique("Work laptop");

    await signIn(page, people.viewer, `/w/${people.workspaceId}/plugin-access`);
    await page.getByRole("textbox", { name: /^Name/ }).fill(name);
    await page.getByRole("spinbutton", { name: /^Days until it expires/ }).fill("7");
    await page.getByRole("button", { name: "Mint" }).click();

    await expect(page.getByRole("status").filter({ hasText: "Copy this now" })).toBeVisible();
    const token = (await page.locator("code").first().innerText()).trim();
    expect(token.length).toBeGreaterThan(20);

    const row = page.getByRole("row", { name: new RegExp(name) });
    await expect(row).toContainText("Active");

    // Shown once: after a reload the value is nowhere on the page.
    await page.reload();
    await expect(page.getByText(token)).toHaveCount(0);

    await page.getByRole("row", { name: new RegExp(name) }).getByRole("button", { name: "Revoke" }).click();
    await expect(page.getByRole("row", { name: new RegExp(name) })).toContainText("Ended");
  });

  test("a machine token is not a session: the HTTP API refuses it as a bearer", async ({ people }) => {
    const viewer = await Api.signIn(people.viewer.email, people.viewer.password);

    try {
      const minted = await viewer.invoke("issue_machine_token", {
        workspaceId: people.workspaceId,
        name: unique("Bearer probe"),
        lifetimeDays: 1,
      });

      const http = await anonymous();
      const response = await http.get("/me", { headers: { authorization: `Bearer ${minted.token}` } });
      expect(response.status()).toBe(401);
      await http.dispose();

      await viewer.invoke("revoke_machine_token", { workspaceId: people.workspaceId, tokenId: minted.tokenId });
    } finally {
      await viewer.dispose();
    }
  });

  test("a token lifetime outside 1 to 365 days is refused", async ({ people }) => {
    const viewer = await Api.signIn(people.viewer.email, people.viewer.password);

    try {
      for (const lifetimeDays of [0, 366]) {
        const response = await viewer.call("issue_machine_token", {
          workspaceId: people.workspaceId,
          name: unique("Out of range"),
          lifetimeDays,
        });
        expect(response.status(), `${lifetimeDays} days`).toBe(400);
      }
    } finally {
      await viewer.dispose();
    }
  });
});
