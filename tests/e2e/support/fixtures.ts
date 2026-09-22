import { test as base, expect, type Browser, type Locator, type Page } from "@playwright/test";
import { Api } from "./api";
import { unique } from "./env";
import { cast, type Cast, type Person } from "./people";

export { expect };

export interface Scope {
  workspaceId: string;
  projectId: string;
}

export interface Project extends Scope {
  name: string;
}

interface Fixtures {
  /** The accounts the setup project created. */
  people: Cast;

  /** The bootstrapped administrator, over the HTTP API. */
  admin: Api;

  /** A project of this test's own, so nothing another test does can change what it sees. */
  project: Project;

  /** A work item in {@link project}. */
  workItem: { workItemId: string; key: string; title: string };
}

export const test = base.extend<Fixtures>({
  // Playwright requires the first argument of a fixture to be destructured, even when it is unused.
  // eslint-disable-next-line no-empty-pattern
  people: async ({}, use) => {
    await use(cast());
  },

  admin: async ({ people }, use) => {
    const api = await Api.signIn(people.admin.email, people.admin.password);
    await use(api);
    await api.dispose();
  },

  project: async ({ admin, people }, use) => {
    const name = unique("E2E project");
    const created = await admin.invoke("create_project", { workspaceId: people.workspaceId, name });
    await use({ workspaceId: people.workspaceId, projectId: created.projectId, name });
  },

  workItem: async ({ admin, project }, use) => {
    const key = `E2E-${Math.floor(Math.random() * 90_000) + 10_000}`;
    const title = unique("Checkout redesign");

    const created = await admin.invoke("create_work_item", {
      scope: { workspaceId: project.workspaceId, projectId: project.projectId },
      key,
      type: "Enhance",
      title,
      goal: "Customers finish paying in fewer steps.",
      inScope: "The payment page.",
      exclusions: "Refunds.",
    });

    await use({ workItemId: created.workItemId, key, title });
  },
});

/**
 * Signs in through the sign-in form, starting at the page the test wants to end up on.
 *
 * Every path shows the sign-in form to somebody signed out, and signing in re-reads the session in
 * place, so the person lands where they were going without depending on the workspace picker —
 * which only redirects when they hold exactly one workspace.
 */
export async function signIn(page: Page, person: Person, path = `/w/${cast().workspaceId}`): Promise<void> {
  await page.goto(path);

  await page.getByRole("textbox", { name: /^Email/ }).fill(person.email);
  await page.getByLabel(/^Password/).fill(person.password);
  await clickUntilSent(page.getByRole("button", { name: "Sign in", exact: true }), "/auth/sign-in");

  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
}

/**
 * Clicks a signed-out form's submit button, and clicks it again if its request never leaves.
 *
 * Firefox, on both CI runners, sometimes takes the click on a signed-out page's submit button and
 * sends nothing: no request, the form still filled, no error. Traces show it on the sign-in form
 * (run 35602268050) and on setting a password (35681490953); the recovery form and a sign-in after
 * a reset failed the same way where the trace was lost. It happens on a page just loaded. It has
 * not been seen on a signed-in screen, nor in Chromium or WebKit. **The cause is not identified.**
 * So the test goes on only once the request is seen. The click is repeated only when nothing left
 * within five seconds, so a request that did leave is not sent twice.
 */
export async function clickUntilSent(button: Locator, pathname: string): Promise<void> {
  const page = button.page();
  await expect(async () => {
    const sent = page.waitForRequest(
      (request) => request.method() === "POST" && new URL(request.url()).pathname === pathname,
      { timeout: 5_000 },
    );
    await button.click();
    await sent;
  }).toPass({ timeout: 30_000 });
}

/** A second person at the same time, in a browser context of their own. */
export async function openAs(browser: Browser, person: Person, path?: string): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, person, path);
  return page;
}

/** Just the two identifiers, so a project fixture never sends its name along as part of a scope. */
export function scopeOf(scope: Scope): Scope {
  return { workspaceId: scope.workspaceId, projectId: scope.projectId };
}

export function projectPath(scope: Scope, rest = ""): string {
  return `/w/${scope.workspaceId}/p/${scope.projectId}${rest}`;
}

/** A draft, written straight through the API, for tests about what happens to one afterwards. */
export async function draft(
  api: Api,
  scope: Scope,
  workItemId: string,
  content: { title?: string; body?: string; frontMatter?: Record<string, string> } = {},
): Promise<{ recordId: string; title: string; body: string }> {
  const title = content.title ?? unique("Use idempotency keys for payment retries");
  const body =
    content.body ??
    "Retries of a payment call carry the key of the first attempt, so a timeout never charges twice.";

  const created = await api.invoke("create_draft", {
    scope: scopeOf(scope),
    workItemId,
    kind: "Decision",
    title,
    body,
    frontMatter: content.frontMatter ?? {},
    provenance: {
      sourceKind: "HumanAuthored",
      sourceLocator: "Architecture review, week 37",
      author: "E2E suite",
      recordedAt: new Date().toISOString(),
      evidence: [],
    },
  });

  return { recordId: created.recordId, title, body };
}

/** Takes a draft all the way to Published, as two different people. */
export async function publish(author: Api, reviewer: Api, project: Scope, recordId: string): Promise<void> {
  const scope = scopeOf(project);
  await author.invoke("submit_for_approval", { scope, recordId });

  const history = await reviewer.invoke("view_record_history", { scope, recordId });
  const newest = history.revisions.reduce((a, b) => (a.number > b.number ? a : b));

  await reviewer.invoke("approve_record", { scope, recordId, approvedContentHash: newest.contentHash });
  await reviewer.invoke("publish_record", { scope, recordId });
}
