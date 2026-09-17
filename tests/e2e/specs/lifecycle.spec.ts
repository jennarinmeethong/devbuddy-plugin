import { Api } from "../support/api";
import { unique } from "../support/env";
import { draft, expect, openAs, projectPath, scopeOf, signIn, test } from "../support/fixtures";

/**
 * A knowledge record from first draft to archive, carried by the people whose job each step is.
 * This is the story the product exists for, so it runs through the pages rather than the API.
 */
test("a draft is written, revised, sent back, approved, published and archived", async ({
  page,
  browser,
  people,
  project,
  workItem,
}) => {
  test.slow();

  const title = unique("Retry payments with the original idempotency key");

  await test.step("a contributor writes a draft from the work item", async () => {
    await signIn(page, people.contributor, projectPath(project, `/work/${workItem.workItemId}`));

    const form = page.locator("section", { has: page.getByRole("heading", { name: "Write a new draft" }) });
    await form.getByRole("combobox", { name: /^Kind/ }).selectOption({ label: "Decision" });
    await form.getByRole("textbox", { name: /^Source/ }).fill("Payments guild, 2026-09-17");
    await form.getByRole("textbox", { name: /^Title/ }).fill(title);
    await form.getByRole("textbox", { name: /^Body/ }).fill("A retried charge reuses the key of the first attempt.");

    await form.getByRole("button", { name: "Add a field" }).click();
    await form.getByRole("textbox", { name: "Field 1 name" }).fill("owner");
    await form.getByRole("textbox", { name: "Field 1 value" }).fill("payments");

    // Two fields with one name are caught before anything is sent.
    await form.getByRole("button", { name: "Add a field" }).click();
    await form.getByRole("textbox", { name: "Field 2 name" }).fill("owner");
    await expect(form.getByRole("alert")).toContainText("named twice");
    await expect(form.getByRole("button", { name: "Save draft" })).toBeDisabled();
    await form.getByRole("button", { name: "Remove field 2" }).click();

    await form.getByRole("button", { name: "Save draft" }).click();

    await expect(page).toHaveURL(/\/records\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(title);
    await expect(page.getByText("Revision 1 · never published")).toBeVisible();
    await expect(page.getByRole("definition").filter({ hasText: "payments" })).toBeVisible();
    await expect(page.getByText("Draft", { exact: true }).first()).toBeVisible();
  });

  const recordUrl = page.url();

  await test.step("the contributor revises it, and the front matter is carried over", async () => {
    await page.getByRole("button", { name: "Edit this draft" }).click();
    await expect(page.getByText("Editing revision 1. Saving adds revision 2")).toBeVisible();
    await expect(page.getByRole("textbox", { name: "Field 1 value" })).toHaveValue("payments");

    await page.getByRole("textbox", { name: /^Body/ }).fill(
      "A retried charge reuses the key of the first attempt, for 24 hours.",
    );
    await page.getByRole("button", { name: "Save as a new revision" }).click();

    await expect(page.getByText("Revision 2 · never published")).toBeVisible();
    await expect(page.getByText("for 24 hours.")).toBeVisible();
    await expect(page.getByText(/^Revision 1 — /)).toBeVisible();
    await expect(page.getByText(/^Revision 2 — /)).toBeVisible();
  });

  await test.step("the contributor submits it, and is not offered the approval", async () => {
    await page.getByRole("button", { name: "Submit for approval" }).click();
    await expect(page.getByRole("heading", { name: "Submit for approval" })).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Approve or send back" })).toHaveCount(0);
    await expect(page.getByText("PendingApproval", { exact: true })).toBeVisible();
  });

  const reviewer = await openAs(browser, people.reviewer, new URL(recordUrl).pathname);

  await test.step("a reviewer finds it in the queue and sends it back with a reason", async () => {
    await reviewer.goto(projectPath(project, "/records"));
    await expect(reviewer.getByRole("combobox", { name: "Status" })).toHaveValue("PendingApproval");
    await reviewer.getByRole("link", { name: title }).click();

    await expect(reviewer.getByText("You are approving revision 2")).toBeVisible();
    await reviewer.getByRole("textbox", { name: /^Or send it back/ }).fill("Say what happens after the 24 hours.");
    await reviewer.getByRole("button", { name: "Request a correction" }).click();
    await expect(reviewer.getByRole("heading", { name: "Approve or send back" })).toHaveCount(0);
  });

  await test.step("the contributor sees why, revises again and resubmits", async () => {
    await page.reload();
    await expect(page.getByRole("alert").filter({ hasText: "Say what happens after the 24 hours." })).toBeVisible();

    await page.getByRole("button", { name: "Edit this draft" }).click();
    await page.getByRole("textbox", { name: /^Body/ }).fill(
      "A retried charge reuses the key of the first attempt for 24 hours; after that it is a new charge.",
    );
    await page.getByRole("button", { name: "Save as a new revision" }).click();
    await expect(page.getByText("Revision 3 · never published")).toBeVisible();

    // The reason stays in the history, against the revision it was about.
    await expect(page.getByText(/Sent back .* Say what happens after the 24 hours\./).first()).toBeVisible();

    await page.getByRole("button", { name: "Submit for approval" }).click();
    await expect(page.getByText("PendingApproval", { exact: true })).toBeVisible();
  });

  await test.step("the reviewer approves exactly revision 3 and publishes it", async () => {
    await reviewer.reload();
    const hash = await reviewer
      .locator("section", { has: reviewer.getByRole("heading", { name: "Approve or send back" }) })
      .locator("code")
      .innerText();
    expect(hash).toMatch(/^[0-9a-f]{64}$/i);

    await reviewer.getByRole("button", { name: "Approve revision 3" }).click();
    await expect(reviewer.getByRole("heading", { name: "Publish" })).toBeVisible();
    await reviewer.getByRole("button", { name: "Publish", exact: true }).click();

    await expect(reviewer.getByText("Revision 3 · published")).toBeVisible();
    await expect(reviewer.getByText(/^Approved .* by /).first()).toBeVisible();
    await expect(reviewer.getByText(hash).first()).toBeVisible();
  });

  await test.step("a viewer reads the published record, and nothing lets them change it", async () => {
    const viewer = await openAs(browser, people.viewer, projectPath(project, "/records"));
    await viewer.getByRole("combobox", { name: "Status" }).selectOption({ label: "Published" });
    await viewer.getByRole("link", { name: title }).click();

    await expect(viewer.getByText("after that it is a new charge.")).toBeVisible();
    await expect(viewer.getByRole("heading", { name: "History" })).toBeVisible();
    for (const panel of ["Revise this draft", "Submit for approval", "Approve or send back", "Publish", "Archive"]) {
      await expect(viewer.getByRole("heading", { name: panel, exact: true })).toHaveCount(0);
    }
    await viewer.context().close();
  });

  await test.step("the reviewer archives it, behind a confirmation", async () => {
    await reviewer.getByRole("button", { name: "Archive", exact: true }).click();
    await reviewer.getByRole("button", { name: "Cancel" }).click();
    await expect(reviewer.getByRole("button", { name: "Archive for good" })).toHaveCount(0);

    await reviewer.getByRole("button", { name: "Archive", exact: true }).click();
    await reviewer.getByRole("button", { name: "Archive for good" }).click();
    await expect(reviewer.getByRole("heading", { name: "Archive", exact: true })).toHaveCount(0);
    await expect(reviewer.getByText("Archived", { exact: true })).toBeVisible();
  });

  await test.step("the work item lists the record with its final status", async () => {
    await page.goto(projectPath(project, `/work/${workItem.workItemId}`));
    await expect(page.getByRole("row", { name: new RegExp(title) })).toContainText("Archived");
  });

  await reviewer.context().close();
});

test("an approval for content the reviewer did not read is refused", async ({ people, project, workItem }) => {
  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const reviewer = await Api.signIn(people.reviewer.email, people.reviewer.password);
  const scope = scopeOf(project);

  try {
    const record = await draft(contributor, scope, workItem.workItemId);
    const first = await contributor.invoke("view_record_history", { scope, recordId: record.recordId });
    const staleHash = first.revisions[0]!.contentHash;

    await contributor.invoke("revise_draft", {
      scope,
      recordId: record.recordId,
      title: record.title,
      body: `${record.body} Revised after the reviewer looked.`,
      frontMatter: {},
      provenance: {
        sourceKind: "HumanAuthored",
        sourceLocator: "Architecture review, week 37",
        author: "E2E suite",
        recordedAt: new Date().toISOString(),
        evidence: [],
      },
    });
    await contributor.invoke("submit_for_approval", { scope, recordId: record.recordId });

    const stale = await reviewer.call("approve_record", {
      scope,
      recordId: record.recordId,
      approvedContentHash: staleHash,
    });
    expect(stale.status()).toBe(409);

    // Nothing was approved, so nothing can be published.
    const publish = await reviewer.call("publish_record", { scope, recordId: record.recordId });
    expect(publish.ok()).toBe(false);

    const after = await reviewer.invoke("view_record_history", { scope, recordId: record.recordId });
    expect(after.status).toBe("PendingApproval");
    expect(after.revisions.every((revision) => !revision.approval)).toBe(true);
  } finally {
    await contributor.dispose();
    await reviewer.dispose();
  }
});

test("a step taken in the wrong state is refused by a rule", async ({ people, project, workItem }) => {
  const reviewer = await Api.signIn(people.reviewer.email, people.reviewer.password);
  const scope = scopeOf(project);

  try {
    const record = await draft(reviewer, scope, workItem.workItemId);

    // A draft that was never submitted cannot be published, whoever asks.
    const published = await reviewer.call("publish_record", { scope, recordId: record.recordId });
    expect(published.status()).toBe(409);

    await reviewer.invoke("archive_record", { scope, recordId: record.recordId });

    // An archived record refuses every further change.
    const submitted = await reviewer.call("submit_for_approval", { scope, recordId: record.recordId });
    expect(submitted.status()).toBe(409);
  } finally {
    await reviewer.dispose();
  }
});
