import { expect, projectPath, signIn, test } from "../support/fixtures";

/**
 * SB-17 end to end: content carrying a credential is refused before anything is stored, whichever
 * door it comes through, and text checked on the maintenance screen names a rule, never the match.
 *
 * The key is assembled at run time so this file is not itself a finding for a repository scanner.
 * It matches the AWS access key shape the server's rule set looks for.
 */
const leakedKey = ["AKIA", "E2ETESTFIXTURE00"].join("");

test("a draft carrying a credential is blocked and nothing is kept", async ({ page, people, project, workItem }) => {
  await signIn(page, people.contributor, projectPath(project, `/work/${workItem.workItemId}`));

  const form = page.locator("section", { has: page.getByRole("heading", { name: "Write a new draft" }) });
  await form.getByRole("textbox", { name: /^Source/ }).fill("Incident channel");
  await form.getByRole("textbox", { name: /^Title/ }).fill("How to reach the payments bucket");
  await form.getByRole("textbox", { name: /^Body/ }).fill(`Use access key ${leakedKey} for the bucket.`);
  await form.getByRole("button", { name: "Save draft" }).click();

  const refusal = form.getByRole("alert");
  await expect(refusal).toContainText("Blocked content");
  await expect(refusal).not.toContainText(leakedKey);
  await expect(page).toHaveURL(new RegExp(`/work/${workItem.workItemId}$`));

  await page.reload();
  await expect(page.getByText("Nothing written yet.")).toBeVisible();
});

test("an attachment carrying a credential is refused and nothing is stored", async ({ page, people, project }) => {
  await signIn(page, people.contributor, projectPath(project, "/evidence"));

  await page.getByLabel(/^File/).setInputFiles({
    name: "deploy.log",
    mimeType: "text/plain",
    buffer: Buffer.from(`step 3: exporting AWS_ACCESS_KEY_ID=${leakedKey}\n`),
  });
  await page.getByRole("textbox", { name: /^What it shows/ }).fill("The failed deployment");
  await page.getByRole("button", { name: "Attach" }).click();

  await expect(page.getByRole("alert")).toContainText("Blocked content");

  await page.reload();
  await expect(page.getByText("Nothing attached yet.")).toBeVisible();
});

test("checking text names the rule and the line, and redaction removes the value", async ({
  page,
  people,
  project,
}) => {
  await signIn(page, people.admin, projectPath(project, "/maintenance"));

  const panel = page.locator("section", { has: page.getByRole("heading", { name: "Check text" }) });
  const text = panel.getByRole("textbox", { name: /^Text/ });

  await text.fill("Nothing sensitive here.\nJust notes.");
  await panel.getByRole("button", { name: "Look for secrets" }).click();
  await expect(panel.getByRole("status")).toHaveText("No secrets found.");

  await text.fill(`line one\naws = ${leakedKey}\nline three`);
  await panel.getByRole("button", { name: "Look for secrets" }).click();
  const findings = panel.getByRole("table");
  await expect(findings).toContainText("aws-access-key-id");
  await expect(findings.getByRole("row").nth(1)).toContainText("2");
  await expect(findings).not.toContainText(leakedKey);

  await panel.getByRole("button", { name: "Redact it" }).click();
  const redacted = panel.locator("pre");
  await expect(redacted).toContainText("line three");
  await expect(redacted).not.toContainText(leakedKey);
  await expect(panel.getByText(/finding\(s\) redacted/)).toBeVisible();
});
