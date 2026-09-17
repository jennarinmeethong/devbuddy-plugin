import { anonymous } from "../support/api";
import { expect, signIn, test } from "../support/fixtures";

/**
 * The API host serves the web client from its own image. The part that could break silently is the
 * fallback for the client's routes: it must never put an HTML page in front of an endpoint that
 * answers with JSON, a refusal or a 404.
 */

test("a deep link into the client is served the application", async ({ people }) => {
  const http = await anonymous();

  try {
    for (const path of ["/", "/set-password", `/w/${people.workspaceId}/p/00000000-0000-0000-0000-000000000000/records`]) {
      const response = await http.get(path);
      expect(response.status(), path).toBe(200);
      expect(response.headers()["content-type"], path).toContain("text/html");
    }
  } finally {
    await http.dispose();
  }
});

test("API routes answer as the API, never with the client's HTML", async ({ admin }) => {
  const http = await anonymous();

  try {
    const liveness = await http.get("/health");
    expect(liveness.headers()["content-type"]).toContain("application/json");

    for (const [method, path] of [
      ["GET", "/me"],
      ["GET", "/operations"],
      ["POST", "/operations/list_projects"],
      ["POST", "/auth/refresh"],
    ] as const) {
      const response = method === "GET" ? await http.get(path) : await http.post(path, { data: {} });
      expect(response.status(), `${method} ${path}`).toBeGreaterThanOrEqual(400);
      expect(response.headers()["content-type"] ?? "", `${method} ${path}`).not.toContain("text/html");
    }
  } finally {
    await http.dispose();
  }

  const unknown = await admin.call("no_such_operation", {});
  expect(unknown.status()).toBe(404);
  expect(unknown.headers()["content-type"]).not.toContain("text/html");
  expect(((await unknown.json()) as { detail: string }).detail).toContain("No operation named no_such_operation");
});

test("the client's build carries no source maps", async ({ page }) => {
  const scripts: string[] = [];
  page.on("response", (response) => {
    if (response.url().endsWith(".js")) {
      scripts.push(response.url());
    }
  });

  await page.goto("/");
  await expect(page.getByRole("button", { name: "Sign in", exact: true })).toBeVisible();
  expect(scripts.length).toBeGreaterThan(0);

  const http = await anonymous();

  try {
    for (const script of scripts) {
      const body = await (await http.get(script)).text();
      expect(body, script).not.toContain("sourceMappingURL");
      expect((await http.get(`${script}.map`)).headers()["content-type"] ?? "", `${script}.map`).not.toContain(
        "json",
      );
    }
  } finally {
    await http.dispose();
  }
});

test("an unknown client route inside a workspace falls back to the workspace", async ({ page, people }) => {
  await signIn(page, people.viewer, `/w/${people.workspaceId}/no-such-screen`);
  await expect(page).toHaveURL(new RegExp(`/w/${people.workspaceId}$`));
  await expect(page.getByRole("navigation", { name: "Workspace" })).toBeVisible();
});

test("the page raises no script errors while a person works through it", async ({ page, people }) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error" && !/Failed to load resource/.test(message.text())) {
      errors.push(message.text());
    }
  });

  await signIn(page, people.admin);
  const nav = page.getByRole("navigation", { name: "Workspace" });

  for (const screen of ["Members", "Teams", "Workspaces", "Audit", "Health", "Plugin access", "Projects"]) {
    await nav.getByRole("link", { name: screen }).click();
    await expect(page.getByRole("main")).not.toContainText("Loading…");
  }

  expect(errors).toEqual([]);
});
