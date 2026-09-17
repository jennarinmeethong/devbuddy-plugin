import { randomBytes } from "node:crypto";
import { Api } from "../support/api";
import { draft, expect, projectPath, publish, signIn, test } from "../support/fixtures";

test("full-text search finds published knowledge by default, and drafts only when asked", async ({
  page,
  people,
  project,
  workItem,
}) => {
  // A word nothing else in the installation contains, so every hit is one of these two.
  const word = `zephyr${randomBytes(3).toString("hex")}`;

  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const reviewer = await Api.signIn(people.reviewer.email, people.reviewer.password);

  try {
    const published = await draft(contributor, project, workItem.workItemId, {
      title: `Published ${word} decision`,
      body: `The ${word} queue is drained before a deployment.`,
    });
    await publish(contributor, reviewer, project, published.recordId);

    await draft(contributor, project, workItem.workItemId, {
      title: `Unpublished ${word} idea`,
      body: `Maybe the ${word} queue should be partitioned.`,
    });
  } finally {
    await contributor.dispose();
    await reviewer.dispose();
  }

  await signIn(page, people.viewer, projectPath(project, "/search"));

  const ask = page.getByRole("textbox", { name: /^Question or words/ });
  const searchText = page.getByRole("button", { name: "Search the text" });
  await expect(searchText).toBeDisabled();

  await ask.fill(word);
  await expect(page.getByRole("checkbox", { name: "Published" })).toBeChecked();
  await searchText.click();

  const results = page.locator("section", { has: page.getByRole("heading", { name: "Full-text results" }) });
  await expect(results.getByRole("link", { name: `Published ${word} decision` })).toBeVisible();
  await expect(results.getByRole("link", { name: `Unpublished ${word} idea` })).toHaveCount(0);

  // No status ticked means every status, drafts included.
  await page.getByRole("checkbox", { name: "Published" }).uncheck();
  await searchText.click();
  await expect(results.getByRole("link", { name: `Unpublished ${word} idea` })).toBeVisible();
  await expect(results.getByRole("link", { name: `Published ${word} decision` })).toBeVisible();

  // A kind nothing here has.
  await page.getByRole("checkbox", { name: "Handover" }).check();
  await searchText.click();
  await expect(results.getByText("Nothing matched.")).toBeVisible();

  // A hit opens the record.
  await page.getByRole("checkbox", { name: "Handover" }).uncheck();
  await searchText.click();
  await results.getByRole("link", { name: `Published ${word} decision` }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(`Published ${word} decision`);
});

test("semantic search says why it cannot answer instead of showing an empty list", async ({
  page,
  people,
  project,
}) => {
  await signIn(page, people.viewer, projectPath(project, "/search"));

  await page.getByRole("textbox", { name: /^Question or words/ }).fill("how are payment retries made safe");
  await page.getByRole("button", { name: "Search by meaning" }).click();

  const results = page.locator("section", { has: page.getByRole("heading", { name: "Results by meaning" }) });

  // The stack under test configures no embedding provider, which is the shipped default.
  await expect(results.getByRole("status")).not.toBeEmpty();
  await expect(results.getByRole("table")).toHaveCount(0);
  await expect(results.getByText("Nothing close enough.")).toHaveCount(0);
});

test("the review queue and status filter agree with the lifecycle", async ({ page, people, project, workItem }) => {
  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);

  try {
    const waiting = await draft(contributor, project, workItem.workItemId, { title: "Waiting for a reviewer" });
    await contributor.invoke("submit_for_approval", {
      scope: { workspaceId: project.workspaceId, projectId: project.projectId },
      recordId: waiting.recordId,
    });
    await draft(contributor, project, workItem.workItemId, { title: "Still a draft" });
  } finally {
    await contributor.dispose();
  }

  await signIn(page, people.reviewer, projectPath(project, "/records"));
  const table = page.getByRole("table");

  await expect(table.getByRole("link")).toHaveText(["Waiting for a reviewer"]);

  const status = page.getByRole("combobox", { name: "Status" });
  await status.selectOption({ label: "Draft" });
  await expect(table.getByRole("link")).toHaveText(["Still a draft"]);
  await expect(table).toContainText("(nothing published)");
  await expect(table).not.toContainText("undefined");

  await status.selectOption({ label: "Every status" });
  await expect(table.getByRole("link")).toHaveCount(2);

  await status.selectOption({ label: "Archived" });
  await expect(page.getByText("No records match that filter.")).toBeVisible();
});
