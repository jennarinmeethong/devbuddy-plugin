import { env } from "../support/env";
import { expect, projectPath, signIn, test } from "../support/fixtures";
import { writeRepository } from "../support/git";

/**
 * Read-only analysis over a working copy the API can see.
 *
 * The suite writes the working copy into the directory the API mounts read-only, which is only
 * possible when it runs beside the stack (run.sh does that). Pointed anywhere else these skip.
 */
const first = {
  message: "Add the payment service",
  tag: "v1.0.0",
  files: {
    "README.md": "# Payments\n\nTakes card payments.\n",
    "src/payments.ts": "export function charge(amount: number) {\n  return amount;\n}\n",
    "docs/adr/0001-idempotency.md": "# 1. Idempotency keys\n\nAccepted.\n",
  },
};

const second = {
  message: "Retry declined payments",
  tag: "v1.1.0",
  files: {
    ...first.files,
    "src/payments.ts": "export function charge(amount: number, attempt = 1) {\n  return attempt <= 3 ? amount : 0;\n}\n",
    "tests/payments.test.ts": "import { charge } from '../src/payments';\n",
  },
};

test("a working copy is listed, analysed, compared, synchronised and its change impact worked out", async ({
  page,
  people,
  project,
}) => {
  test.skip(!env.projectsRoot, "DEVBUDDY_E2E_PROJECTS is not set, so no working copy can be planted.");
  test.slow();

  const repository = writeRepository(env.projectsRoot!, project.projectId, [first, second]);
  const [initial, head] = repository.commits as [string, string];

  await signIn(page, people.admin, projectPath(project, "/analysis"));

  await test.step("the repository is offered by identifier, with no server path", async () => {
    const listed = page.locator("section", {
      has: page.getByRole("heading", { name: "Repositories this project can read" }),
    });
    await expect(listed.getByRole("row", { name: new RegExp(repository.repositoryId) })).toContainText(
      "Mounted working copy",
    );
    await expect(page.locator("body")).not.toContainText("/srv/projects");
  });

  await test.step("the seven analyses each answer", async () => {
    const panel = page.locator("section", { has: page.getByRole("heading", { name: "Run an analysis" }) });
    const what = panel.getByRole("combobox", { name: /^What to look at/ });
    await panel.getByRole("combobox", { name: /^Repository/ }).selectOption(repository.repositoryId);

    for (const label of [
      "The project as a whole",
      "Code",
      "Documents",
      "Architecture",
      "Git history",
      "Work items",
      "Test evidence",
    ]) {
      await what.selectOption({ label });
      await panel.getByRole("button", { name: "Analyse" }).click();
      await expect(panel.getByRole("alert"), label).toHaveCount(0);
      await expect(panel.locator("p.text-sm").first(), label).not.toBeEmpty();
    }
  });

  await test.step("a path that leaves the repository is refused", async () => {
    const panel = page.locator("section", { has: page.getByRole("heading", { name: "Run an analysis" }) });
    await panel.getByRole("combobox", { name: /^What to look at/ }).selectOption({ label: "Code" });
    await panel.getByRole("textbox", { name: /^Path inside it/ }).fill("../../../etc");
    await panel.getByRole("button", { name: "Analyse" }).click();
    await expect(panel.getByRole("alert")).toBeVisible();
    await panel.getByRole("textbox", { name: /^Path inside it/ }).fill("");
  });

  await test.step("the change impact of the second commit names the files it touched", async () => {
    const panel = page.locator("section", { has: page.getByRole("heading", { name: "What a change affects" }) });
    await panel.getByRole("textbox", { name: /^Commit or range/ }).fill(head);
    await panel.getByRole("button", { name: "Work out the impact" }).click();

    const changed = panel.getByRole("heading", { name: /^Changed paths/ });
    await expect(changed).toHaveText("Changed paths (2)");
    const paths = panel.getByRole("listitem");
    await expect(paths).toHaveCount(2);
    await expect(paths.filter({ hasText: "src/payments.ts" })).toHaveCount(1);
    await expect(paths.filter({ hasText: "tests/payments.test.ts" })).toHaveCount(1);
  });

  await test.step("two tags are resolved to their commits and compared", async () => {
    const panel = page.locator("section", { has: page.getByRole("heading", { name: "Compare two references" }) });
    await panel.getByRole("textbox", { name: /^Earlier/ }).fill("v1.0.0");
    await panel.getByRole("textbox", { name: /^Later/ }).fill("v1.1.0");
    await panel.getByRole("button", { name: "Compare" }).click();

    await expect(panel).toContainText(initial);
    await expect(panel).toContainText(head);
    await expect(panel.getByRole("alert")).toHaveCount(0);
  });

  await test.step("synchronising reads where the repository stands, and says what a working copy cannot", async () => {
    const panel = page.locator("section", {
      has: page.getByRole("heading", { name: "Synchronise a repository" }),
    });
    await panel.getByRole("button", { name: "Synchronise" }).click();

    await expect(panel.getByRole("definition").first()).toHaveText(head);
    await expect(panel).toContainText("Not available from a working copy");
  });
});

test("a project with no working copy says how to make one reachable", async ({ page, people, project }) => {
  await signIn(page, people.contributor, projectPath(project, "/analysis"));

  await expect(page.getByRole("status").filter({ hasText: "No repository is reachable" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "What a change affects" })).toHaveCount(0);

  // A contributor does not manage sources, so is never offered synchronisation.
  await expect(page.getByRole("heading", { name: "Synchronise a repository" })).toHaveCount(0);
});
