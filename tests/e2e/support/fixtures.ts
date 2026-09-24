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

/**
 * Phase 14, C3: with DEVBUDDY_E2E_FIRST_CLICK set, every page records the input events it receives,
 * so a click that sent nothing can say whether the page saw it at all.
 */
function recordEvents(): void {
  const log: string[] = [];
  const start = performance.now();
  const at = () => Math.round(performance.now() - start);
  (window as unknown as { __firstClick: string[] }).__firstClick = log;
  for (const type of ["pointerdown", "mousedown", "mouseup", "click", "submit", "invalid", "focusin", "blur"]) {
    window.addEventListener(
      type,
      (event) => {
        const target = event.target as HTMLElement | null;
        const name = target?.getAttribute?.("name");
        const label = target?.tagName === "BUTTON" ? `(${target.textContent?.trim()})` : "";
        log.push(`${at()} ${type} ${target?.tagName?.toLowerCase() ?? "window"}${name ? `[${name}]` : ""}${label}`);
      },
      true,
    );
  }
  const fetch = window.fetch.bind(window);
  window.fetch = (input, init) => {
    log.push(`${at()} fetch ${init?.method ?? "GET"} ${typeof input === "string" ? input : String(input)}`);
    return fetch(input, init);
  };
  window.addEventListener("load", () => log.push(`${at()} load`));
}

export const test = base.extend<Fixtures>({
  context: async ({ context }, use) => {
    if (process.env.DEVBUDDY_E2E_FIRST_CLICK) {
      await context.addInitScript(recordEvents);
    }
    await use(context);
  },

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
 * Firefox sometimes takes the click on a signed-out page's submit button and sends nothing: no
 * request, the form still filled, no error. It was seen on the sign-in, set-password and recovery
 * forms, in CI only, never in Chromium or WebKit. **Phase 14, C3 found where it is lost, and it is
 * not in this client.** With `DEVBUDDY_E2E_FIRST_CLICK` set, every page records its input events.
 * In three lost clicks (CI runs 35998589217, 36001555287 and 36001564458), each on the arm64
 * runner, the page received the `mouseup` and never the `pointerdown`, `mousedown` or `click`. The
 * press was dropped before it reached the document, where no page code can see it. The same
 * recorder saw 922 clicks on jmhp and about 348 on the x64 runner, all delivered.
 * `docs/plan-phase-14.md` lists what was ruled out. So the test goes on only once the request is
 * seen. The click is repeated only when nothing left within five seconds, so a request that did
 * leave is not sent twice.
 */
export async function clickUntilSent(button: Locator, pathname: string): Promise<void> {
  const page = button.page();
  let attempts = 0;
  await expect(async () => {
    attempts++;
    const sent = page.waitForRequest(
      (request) => request.method() === "POST" && new URL(request.url()).pathname === pathname,
      { timeout: 5_000 },
    );
    await button.click();
    try {
      await sent;
    } catch (failure) {
      if (process.env.DEVBUDDY_E2E_FIRST_CLICK) {
        const log = await page.evaluate(() => (window as unknown as { __firstClick?: string[] }).__firstClick ?? []);
        console.log(`CLICK-LOST ${pathname} attempt ${attempts} ${JSON.stringify(log)}`);
      }
      throw failure;
    }
  }).toPass({ timeout: 30_000 });
  if (process.env.DEVBUDDY_E2E_FIRST_CLICK) {
    console.log(`CLICK-ATTEMPTS ${attempts} ${pathname} ${button.page().context().browser()?.browserType().name()}`);
  }
}

/** A second person at the same time, in a browser context of their own. */
export async function openAs(browser: Browser, person: Person, path?: string): Promise<Page> {
  const context = await browser.newContext();
  if (process.env.DEVBUDDY_E2E_FIRST_CLICK) {
    await context.addInitScript(recordEvents);
  }
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
