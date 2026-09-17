import { readFile } from "node:fs/promises";
import { anonymous, Api } from "../support/api";
import { expect, projectPath, signIn, test } from "../support/fixtures";

test("an artefact is attached, listed, downloaded intact, and cited by a draft", async ({
  page,
  people,
  project,
  workItem,
}) => {
  const content = `build 1.4.0\nall 312 tests passed\n${new Date().toISOString()}\n`;

  await signIn(page, people.contributor, projectPath(project, "/evidence"));
  await expect(page.getByText("Nothing attached yet.")).toBeVisible();

  const attach = page.getByRole("button", { name: "Attach" });
  await expect(attach).toBeDisabled();

  await page.getByLabel(/^File/).setInputFiles({
    name: "build.log",
    mimeType: "text/plain",
    buffer: Buffer.from(content),
  });
  await expect(attach).toBeDisabled();
  await page.getByRole("textbox", { name: /^What it shows/ }).fill("The release build log");
  await attach.click();

  const row = page.getByRole("row", { name: /text\/plain/ });
  await expect(row).toBeVisible();
  await expect(row).toContainText(`${Buffer.byteLength(content)} B`);

  const download = page.waitForEvent("download");
  await row.getByRole("button", { name: "Download" }).click();
  const saved = await (await download).path();
  expect(await readFile(saved, "utf8")).toBe(content);

  // The draft form offers what the project holds, and asks what each cited item shows.
  await page.goto(projectPath(project, `/work/${workItem.workItemId}`));
  const form = page.locator("section", { has: page.getByRole("heading", { name: "Write a new draft" }) });
  await form.getByRole("textbox", { name: /^Source/ }).fill("Release 1.4.0 pipeline");
  await form.getByRole("textbox", { name: /^Title/ }).fill("Release 1.4.0 passed its suite");
  await form.getByRole("textbox", { name: /^Body/ }).fill("The release candidate passed every test.");

  await form.getByRole("checkbox").first().check();
  await expect(form.getByRole("button", { name: "Save draft" })).toBeDisabled();
  await form.getByRole("textbox", { name: /^What evidence .* shows$/ }).fill("The passing build log");
  await form.getByRole("button", { name: "Save draft" }).click();

  await expect(page.getByRole("list", { name: "Evidence" })).toContainText("The passing build log");
  await expect(page.getByText(/1 evidence item\(s\)/)).toBeVisible();
});

test("evidence is reachable only through an authorised request for its own project", async ({
  admin,
  people,
  project,
}) => {
  const scope = { workspaceId: project.workspaceId, projectId: project.projectId };
  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const viewer = await Api.signIn(people.viewer.email, people.viewer.password);

  try {
    const stored = await contributor.upload(
      `/workspaces/${scope.workspaceId}/projects/${scope.projectId}/evidence`,
      { name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from("plain notes") },
      "Meeting notes",
    );
    expect(stored.status()).toBe(200);
    const { evidenceId } = (await stored.json()) as { evidenceId: string };

    // A viewer may read it; a viewer may not add to it.
    const read = await viewer.get(`/workspaces/${scope.workspaceId}/projects/${scope.projectId}/evidence/${evidenceId}`);
    expect(read.status()).toBe(200);
    expect(await read.text()).toBe("plain notes");

    const added = await viewer.upload(
      `/workspaces/${scope.workspaceId}/projects/${scope.projectId}/evidence`,
      { name: "x.txt", mimeType: "text/plain", buffer: Buffer.from("x") },
      "Not allowed",
    );
    expect(added.status()).toBe(403);

    // The same identifier under another project of the same workspace is not found there.
    const other = await admin.invoke("create_project", { workspaceId: people.workspaceId, name: `Other ${evidenceId}` });
    const elsewhere = await contributor.get(
      `/workspaces/${scope.workspaceId}/projects/${other.projectId}/evidence/${evidenceId}`,
    );
    expect(elsewhere.ok()).toBe(false);

    // And there is no anonymous way in.
    const stranger = await anonymous();
    const unauthenticated = await stranger.get(
      `/workspaces/${scope.workspaceId}/projects/${scope.projectId}/evidence/${evidenceId}`,
    );
    expect(unauthenticated.status()).toBe(401);
    await stranger.dispose();
  } finally {
    await contributor.dispose();
    await viewer.dispose();
  }
});
