import { randomBytes } from "node:crypto";
import { Api, anonymous, signInForTokens } from "../support/api";
import { password } from "../support/env";
import { expect, signIn, test } from "../support/fixtures";
import { invite } from "../support/people";

test.describe("signing in and out", () => {
  test("a wrong password is refused with the same words as an unknown address", async ({ page, people }) => {
    await page.goto("/");
    await page.getByRole("textbox", { name: /^Email/ }).fill(people.viewer.email);
    await page.getByLabel(/^Password/).fill(`${people.viewer.password}-wrong`);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();

    const refusal = page.getByRole("alert");
    await expect(refusal).toHaveText("The email or password is not correct.");

    await page.getByRole("textbox", { name: /^Email/ }).fill("nobody-at-all@e2e.devbuddy.test");
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(refusal).toHaveText("The email or password is not correct.");
  });

  test("a person lands in their workspace, keeps the session over a reload, and can sign out", async ({
    page,
    people,
  }) => {
    await signIn(page, people.contributor, "/");

    // One workspace, so the picker is skipped.
    await expect(page).toHaveURL(new RegExp(`/w/${people.workspaceId}$`));
    const header = page.getByRole("banner");
    await expect(header).toContainText(people.workspaceName);
    await expect(header).toContainText("Contributor");
    await expect(header).toContainText(people.contributor.email);

    // The access token lives in memory and is gone after a reload; the refresh token in
    // sessionStorage brings the session back.
    await page.reload();
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();

    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page.getByRole("button", { name: "Sign in", exact: true })).toBeVisible();

    await page.reload();
    await expect(page.getByRole("button", { name: "Sign in", exact: true })).toBeVisible();
    expect(await page.evaluate(() => sessionStorage.getItem("devbuddy.refresh"))).toBeNull();
  });

  test("repeated failures lock the account and the page says until when", async ({ page, admin, people }) => {
    const person = await invite(admin, people.workspaceId, "Viewer");

    await page.goto("/");
    await page.getByRole("textbox", { name: /^Email/ }).fill(person.email);

    for (let attempt = 0; attempt < 5; attempt++) {
      await page.getByLabel(/^Password/).fill(`not-the-password-${attempt}`);
      await page.getByRole("button", { name: "Sign in", exact: true }).click();
      await expect(page.getByRole("alert")).toBeVisible();
    }

    // Even the right password is refused now, with the reason rather than "not correct".
    await page.getByLabel(/^Password/).fill(person.password);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page.getByRole("alert")).toContainText("Locked until");
  });
});

test.describe("recovery and setup tokens", () => {
  test("starting recovery says the same thing whether or not the address has an account", async ({
    page,
    people,
  }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Forgot your password?" }).click();

    for (const email of [people.viewer.email, "no-such-person@e2e.devbuddy.test"]) {
      await page.getByRole("textbox", { name: /^Email/ }).fill(email);
      await page.getByRole("button", { name: "Send a recovery token" }).click();
      await expect(page.getByRole("status")).toContainText("If that address has an account");
    }

    await page.getByRole("button", { name: "Back to sign in" }).click();
    await expect(page.getByRole("button", { name: "Sign in", exact: true })).toBeVisible();
  });

  test("a setup token sets a password once, and is refused the second time", async ({ page, admin, people }) => {
    const email = `setup.${randomBytes(4).toString("hex")}@e2e.devbuddy.test`;
    const created = await admin.invoke("create_user_account", {
      workspaceId: people.workspaceId,
      email,
      displayName: "Setup token person",
      role: "Viewer",
    });

    const chosen = password();

    await page.goto(`/set-password?token=${encodeURIComponent(created.setupToken)}`);
    await expect(page.getByRole("textbox", { name: /^Token/ })).toHaveValue(created.setupToken);

    // Mismatched confirmation is caught before anything is sent.
    await page.getByLabel(/^New password/).fill(chosen);
    await page.getByLabel(/^Confirm password/).fill(`${chosen}x`);
    await page.getByRole("button", { name: "Set password" }).click();
    await expect(page.getByRole("alert")).toHaveText("The two passwords do not match.");

    await page.getByLabel(/^Confirm password/).fill(chosen);
    await page.getByRole("button", { name: "Set password" }).click();
    await expect(page.getByRole("status")).toContainText("Your password is set");

    // The same token again.
    await page.goto(`/set-password?token=${encodeURIComponent(created.setupToken)}`);
    await page.getByLabel(/^New password/).fill(password());
    await page.getByLabel(/^Confirm password/).fill(await page.getByLabel(/^New password/).inputValue());
    await page.getByRole("button", { name: "Set password" }).click();
    await expect(page.getByRole("alert")).toContainText("already used");

    await signIn(page, { ...people.viewer, email, password: chosen });
    await expect(page.getByRole("banner")).toContainText("Viewer");
  });

  test("a too-short password is refused and the token stays usable", async ({ admin, people }) => {
    const created = await admin.invoke("create_user_account", {
      workspaceId: people.workspaceId,
      email: `short.${randomBytes(4).toString("hex")}@e2e.devbuddy.test`,
      displayName: "Short password person",
      role: "Viewer",
    });

    const http = await anonymous();

    try {
      const tooShort = await http.post("/auth/recovery/complete", {
        data: { token: created.setupToken, newPassword: "short" },
      });
      expect(tooShort.status()).toBe(400);

      const fine = await http.post("/auth/recovery/complete", {
        data: { token: created.setupToken, newPassword: password() },
      });
      expect(fine.status()).toBe(204);
    } finally {
      await http.dispose();
    }
  });
});

test.describe("tokens over HTTP", () => {
  test("presenting a spent refresh token ends every session in its chain", async ({ people }) => {
    const http = await anonymous();

    try {
      const first = await signInForTokens(http, people.viewer.email, people.viewer.password);

      const rotated = await http.post("/auth/refresh", { data: { refreshToken: first.refreshToken } });
      expect(rotated.status()).toBe(200);
      const second = (await rotated.json()) as { refreshToken: string };

      const replayed = await http.post("/auth/refresh", { data: { refreshToken: first.refreshToken } });
      expect(replayed.status()).toBe(401);
      expect(((await replayed.json()) as { title: string }).title).toBe("Session ended");

      // The successor was issued honestly, and is revoked all the same.
      const successor = await http.post("/auth/refresh", { data: { refreshToken: second.refreshToken } });
      expect(successor.status()).toBe(401);
    } finally {
      await http.dispose();
    }
  });

  test("a signed-out refresh token cannot be exchanged", async ({ people }) => {
    const api = await Api.signIn(people.viewer.email, people.viewer.password);
    const http = await anonymous();

    try {
      const signedOut = await http.post("/auth/sign-out", { data: { refreshToken: api.refreshToken } });
      expect(signedOut.status()).toBe(204);

      const refreshed = await http.post("/auth/refresh", { data: { refreshToken: api.refreshToken } });
      expect(refreshed.status()).toBe(401);
    } finally {
      await http.dispose();
      await api.dispose();
    }
  });

  test("everything but sign-in and liveness needs a bearer", async () => {
    const http = await anonymous();

    try {
      expect((await http.get("/health")).status()).toBe(200);
      expect((await http.get("/me")).status()).toBe(401);
      expect((await http.get("/operations")).status()).toBe(401);
      expect((await http.post("/operations/list_projects", { data: {} })).status()).toBe(401);
    } finally {
      await http.dispose();
    }
  });
});
