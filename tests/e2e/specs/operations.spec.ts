import { Api } from "../support/api";
import { draft, expect, projectPath, publish, scopeOf, signIn, test } from "../support/fixtures";

test("system health names every component as up, and a backup can be taken", async ({ page, people }) => {
  await signIn(page, people.admin, `/w/${people.workspaceId}/health`);

  const health = page.locator("section", { has: page.getByRole("heading", { name: "System health" }) });
  await expect(health.getByText("Healthy", { exact: true })).toBeVisible();
  await expect(health.getByRole("row")).not.toHaveCount(1);
  await expect(health.getByText("Down", { exact: true })).toHaveCount(0);

  // Whatever a component says about itself, it is never a connection string.
  await expect(health).not.toContainText("Password=");

  await page.getByRole("button", { name: "Back up now" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Backup" })).toContainText("bytes");
});

test("the quality sweeps, the index rebuild and the export report what they did", async ({
  page,
  people,
  project,
  workItem,
}) => {
  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const reviewer = await Api.signIn(people.reviewer.email, people.reviewer.password);

  try {
    // The same decision twice, published, so the duplicate sweep has something to find.
    for (let copy = 0; copy < 2; copy++) {
      const record = await draft(contributor, project, workItem.workItemId, {
        title: "Card payments retry at most three times",
        body: "A declined card is retried at most three times, one minute apart.",
      });
      await publish(contributor, reviewer, project, record.recordId);
    }
  } finally {
    await contributor.dispose();
    await reviewer.dispose();
  }

  await signIn(page, people.admin, projectPath(project, "/maintenance"));

  const sweeps = page.locator("section", { has: page.getByRole("heading", { name: "Quality sweeps" }) });
  await sweeps.getByRole("button", { name: "Check provenance" }).click();
  await expect(sweeps.getByRole("heading", { name: /^Provenance/ })).toBeVisible();

  await sweeps.getByRole("button", { name: "Find duplicates" }).click();
  const duplicates = sweeps.getByRole("heading", { name: /^Duplicates/ });
  await expect(duplicates).toBeVisible();
  await expect(duplicates).not.toHaveText("Duplicates 0");

  await sweeps.getByRole("spinbutton", { name: /^Untouched for/ }).fill("1");
  await sweeps.getByRole("button", { name: "Find stale records" }).click();
  // Written a moment ago, so nothing is a day stale.
  await expect(sweeps.getByRole("heading", { name: /^Stale records/ })).toHaveText("Stale records 0");

  await page.getByRole("button", { name: "Rebuild the index" }).click();
  await expect(page.getByRole("status").filter({ hasText: "indexed" })).toHaveText("2 record(s) indexed.");

  await page.getByRole("button", { name: "Export this project" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Export" })).toContainText(
    "2 record(s), 0 evidence item(s)",
  );
});

test("the audit trail records who did what, with the channel, and never the content", async ({
  page,
  people,
  project,
  workItem,
}) => {
  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const body = "Settlement files are archived for seven years, not five.";

  try {
    await draft(contributor, scopeOf(project), workItem.workItemId, { title: "Settlement retention", body });
    await contributor.call("delete_project", { scope: scopeOf(project) }); // refused, and audited
  } finally {
    await contributor.dispose();
  }

  await signIn(page, people.admin, `/w/${people.workspaceId}/audit`);
  await expect(page.getByText("Choose a project to read its audit history.")).toBeVisible();

  await page.getByRole("combobox", { name: /^Project/ }).selectOption({ label: project.name });
  const table = page.getByRole("table");

  const created = table.getByRole("row").filter({ hasText: "DraftCreated" });
  await expect(created).toHaveCount(1);
  await expect(created).toContainText(people.contributor.userId);
  await expect(created).toContainText("Person");
  await expect(created).toContainText("Succeeded");

  await expect(table.getByRole("row").filter({ hasText: "WorkItemCreated" })).toHaveCount(1);
  await expect(table.getByRole("row").filter({ hasText: "AccessDenied" }).first()).toBeVisible();
  await expect(table).not.toContainText(body);

  await page.getByRole("combobox", { name: /^Channel/ }).selectOption({ label: "Assistants only" });
  await expect(page.getByText("Nothing in that window.")).toBeVisible();

  await page.getByRole("combobox", { name: /^Channel/ }).selectOption({ label: "People only" });
  await expect(table.getByRole("row").filter({ hasText: "DraftCreated" })).toHaveCount(1);
});
