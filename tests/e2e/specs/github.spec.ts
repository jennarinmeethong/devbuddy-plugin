import { request } from "@playwright/test";
import { expect, test } from "../support/fixtures";

/**
 * The GitHub source mode, end to end (Phase 13, C3). Runs only with `DEVBUDDY_E2E_GITHUB=1`, where
 * run.sh starts a second API instance with GitHub:Mode=GitHubApi against a stand-in GitHub and one
 * repository configured for one project. The first instance keeps reading working copies, as the
 * shipped default does.
 */
const github = process.env.DEVBUDDY_E2E_GITHUB_URL ?? "http://api-github:8080";
const stub = process.env.DEVBUDDY_E2E_GITHUB_STUB_URL ?? "http://github-stub:8080";
const projectId = process.env.DEVBUDDY_E2E_GITHUB_PROJECT ?? "";
const repositoryId = process.env.DEVBUDDY_E2E_GITHUB_REPOSITORY ?? "";

test.skip(process.env.DEVBUDDY_E2E_GITHUB !== "1", "Only in the GitHub source mode.");

test("the GitHub mode synchronises and analyses a change through the API, presenting its token", async ({ people }) => {
  const http = await request.newContext({ baseURL: github });

  try {
    const signIn = await http.post("/auth/sign-in", {
      data: { email: process.env.DEVBUDDY_E2E_ADMIN_EMAIL, password: process.env.DEVBUDDY_E2E_ADMIN_PASSWORD },
    });
    expect(signIn.status()).toBe(200);
    const headers = { authorization: `Bearer ${(await signIn.json()).accessToken}` };
    const scope = { workspaceId: people.workspaceId, projectId };

    const listed = await http.post("/operations/list_source_repositories", { data: { scope }, headers });
    expect(listed.status()).toBe(200);
    expect(JSON.stringify(await listed.json())).toContain(repositoryId);

    const synced = await http.post("/operations/sync_sources", { data: { scope, repositoryId }, headers });
    expect(synced.status()).toBe(200);
    const snapshot = await synced.json();
    expect(snapshot.commitId).toBe("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678");
    // One pull request is open; GitHub also lists it under issues, and it is not counted twice.
    expect(snapshot.openPullRequestCount).toBe(1);
    expect(snapshot.openIssueCount).toBe(1);

    const impact = await http.post("/operations/analyze_change_impact", {
      data: { scope, repositoryId, commitOrRange: "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678" },
      headers,
    });
    expect(impact.status()).toBe(200);
    expect(JSON.stringify(await impact.json())).toContain("src/Payments/RetryPolicy.cs");

    // Every request reached the stand-in with the configured token, and only the configured
    // repository was asked about.
    const seen = (await (await fetch(`${stub}/_requests`)).json()) as { path: string; authorization: string | null }[];
    expect(seen.length).toBeGreaterThan(0);
    expect(seen.every((entry) => entry.authorization === "Bearer e2e-github-token")).toBe(true);
    expect(seen.every((entry) => entry.path.startsWith("/repos/octo/demo"))).toBe(true);
  } finally {
    await http.dispose();
  }
});

test("the working-copy instance still reports no open counts for the same project", async ({ admin, people }) => {
  const scope = { workspaceId: people.workspaceId, projectId };
  const listed = await admin.call("list_source_repositories", { scope });

  // The first instance has no GitHub configuration: the repository the second one reads is not one
  // it can reach, so it is not listed.
  expect(JSON.stringify(await listed.json())).not.toContain(repositoryId);
});
