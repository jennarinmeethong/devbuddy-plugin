import { unique } from "../support/env";
import { expect, projectPath, signIn, test } from "../support/fixtures";

test("an administrator creates a project, and it starts closed to AI", async ({ page, people }) => {
  const name = unique("Payments platform");

  await signIn(page, people.admin);
  await page.getByRole("textbox", { name: /^Name/ }).fill(name);
  await page.getByRole("button", { name: "Create", exact: true }).click();

  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toBeVisible();
  await expect(row).toContainText("Denied");
  await expect(page.getByRole("textbox", { name: /^Name/ })).toHaveValue("");

  await row.getByRole("link", { name }).click();
  await expect(page.getByRole("heading", { name: "Work", exact: true })).toBeVisible();
  await expect(page.getByText("Nothing registered yet.")).toBeVisible();
});

test("AI access is opened and closed from the project list, and says which it is", async ({
  page,
  people,
  project,
}) => {
  await signIn(page, people.admin);
  const row = page.getByRole("row", { name: new RegExp(project.name) });

  await row.getByRole("button", { name: "Enable AI access" }).click();
  await expect(row).toContainText("Enabled");
  await expect(row.getByRole("button", { name: "Deny AI access" })).toBeVisible();

  // It is state on the server, not in the page.
  await page.reload();
  await expect(page.getByRole("row", { name: new RegExp(project.name) })).toContainText("Enabled");

  await page.getByRole("row", { name: new RegExp(project.name) }).getByRole("button", { name: "Deny AI access" }).click();
  await expect(page.getByRole("row", { name: new RegExp(project.name) })).toContainText("Denied");
});

test("deleting a project needs its name typed back, and takes its content with it", async ({
  page,
  admin,
  people,
  project,
  workItem,
}) => {
  await signIn(page, people.admin);
  const row = page.getByRole("row", { name: new RegExp(project.name) });

  await row.getByRole("button", { name: "Delete", exact: true }).click();
  const confirm = row.getByRole("button", { name: "Delete for good" });
  await expect(confirm).toBeDisabled();

  const typed = row.getByRole("textbox", { name: `Type ${project.name} to confirm deletion` });
  await typed.fill(project.name.slice(0, -1));
  await expect(confirm).toBeDisabled();

  // Cancel really cancels.
  await row.getByRole("button", { name: "Cancel" }).click();
  await expect(row.getByRole("button", { name: "Delete for good" })).toHaveCount(0);

  await row.getByRole("button", { name: "Delete", exact: true }).click();
  await row.getByRole("textbox", { name: `Type ${project.name} to confirm deletion` }).fill(project.name);
  await row.getByRole("button", { name: "Delete for good" }).click();
  await expect(page.getByRole("link", { name: project.name })).toHaveCount(0);

  const gone = await admin.call("get_work_item", {
    scope: { workspaceId: project.workspaceId, projectId: project.projectId },
    workItemId: workItem.workItemId,
  });
  expect(gone.ok()).toBe(false);
});

/**
 * KNOWN DEFECT, found by this suite on 2026-09-17: a deleted project's pages answer with empty
 * lists rather than a refusal, because nothing checks that a project in a scope exists. Same root
 * cause as the one in permissions.spec.ts. Remove `test.fail` once it is fixed.
 */
test("a deep link to a deleted project shows the server's refusal", async ({ page, admin, people, project }) => {
  test.fail(true, "Known defect: a scope naming a deleted project is not refused (2026-09-17).");

  await admin.invoke("delete_project", { scope: { workspaceId: project.workspaceId, projectId: project.projectId } });

  await signIn(page, people.admin, projectPath(project));
  await expect(page.getByRole("alert")).toBeVisible({ timeout: 5_000 });
});

test("a work item is registered with its goal and scope, and every type is spelled out", async ({
  page,
  people,
  project,
}) => {
  await signIn(page, people.contributor, projectPath(project));

  const type = page.getByRole("combobox", { name: /^Type/ });
  await expect(type.getByRole("option")).toHaveText(["Develop", "Enhance", "Fix a bug", "Change request", "Code review"]);

  const key = `PAY-${Date.now() % 100_000}`;
  const title = unique("Split the payment form");

  await page.getByRole("textbox", { name: /^Key/ }).fill(key);
  await type.selectOption({ label: "Change request" });
  await page.getByRole("textbox", { name: /^Title/ }).fill(title);
  await page.getByRole("textbox", { name: /^Goal/ }).fill("Fewer abandoned checkouts.");
  await page.getByRole("textbox", { name: /^In scope/ }).fill("Card and wallet payments.");
  await page.getByRole("textbox", { name: /^Deliberately excluded/ }).fill("Invoices.");
  await page.getByRole("button", { name: "Register" }).click();

  const row = page.getByRole("row", { name: new RegExp(title) });
  await expect(row).toContainText(key);
  await expect(row).toContainText("Change request");

  await row.getByRole("link", { name: title }).click();
  await expect(page.getByRole("heading", { name: `${key} · ${title}` })).toBeVisible();

  const facts = page.locator("dl");
  await expect(facts).toContainText("Fewer abandoned checkouts.");
  await expect(facts).toContainText("Card and wallet payments.");
  await expect(facts).toContainText("Invoices.");
  await expect(facts).toContainText("ChangeRequest");
  await expect(page.getByText("Nothing written yet.")).toBeVisible();
});

test("a duplicate work item key is refused with the server's reason", async ({ page, people, project, workItem }) => {
  await signIn(page, people.contributor, projectPath(project));

  await page.getByRole("textbox", { name: /^Key/ }).fill(workItem.key);
  await page.getByRole("textbox", { name: /^Title/ }).fill("Same key again");
  await page.getByRole("textbox", { name: /^Goal/ }).fill("Should not be accepted.");
  await page.getByRole("button", { name: "Register" }).click();

  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page.getByRole("row", { name: /Same key again/ })).toHaveCount(0);
});
