import { draft, expect, publish, scopeOf, test } from "../support/fixtures";

/**
 * Embeddings and the sweep, end to end (Phase 13, C1). Runs only in the embeddings mode
 * (`DEVBUDDY_E2E_EMBEDDINGS=1`), where the stack has pgvector, a stand-in model server that keeps
 * everything it is sent, and the real record-embedding-sweep running every five seconds as a Viewer
 * account with its own token.
 *
 * What is asserted is what left the installation, read back from the model server: the published
 * text of a project opened to AI, and never a draft or a closed project's record.
 */
const embedder = process.env.DEVBUDDY_E2E_EMBEDDER_URL ?? "http://embedder:8080";

test.skip(process.env.DEVBUDDY_E2E_EMBEDDINGS !== "1", "Only in the embeddings mode.");
test.describe.configure({ timeout: 180_000 });

async function received(): Promise<string[]> {
  return (await (await fetch(`${embedder}/received`)).json()) as string[];
}

test("a published record in a project opened to AI is embedded, found, and removed when archived", async ({
  admin,
  project,
  workItem,
}) => {
  const scope = scopeOf(project);
  await admin.invoke("enable_project_ai_access", { scope });

  const marker = `zebra${Date.now().toString(36)}`;
  const published = await draft(admin, project, workItem.workItemId, {
    title: `Ledger reconciliation ${marker}`,
    body: `The ${marker} ledger is reconciled nightly against the bank statement.`,
  });
  await publish(admin, admin, project, published.recordId);

  const unpublished = await draft(admin, project, workItem.workItemId, {
    title: "Still a draft",
    body: `draftonly${marker} must never be embedded.`,
  });

  // The sweep runs every five seconds; wait for the published record to become findable.
  let found = false;
  for (let attempt = 0; attempt < 60 && !found; attempt++) {
    const answer = await admin.invoke("search_similar_records", { scope, queryText: `${marker} ledger reconciliation` });
    found = answer.hits.some((hit) => hit.recordId === published.recordId);
    if (!found) {
      await new Promise((resolve) => setTimeout(resolve, 2000));
    }
  }
  expect(found).toBe(true);

  const sent = await received();
  expect(sent.some((text) => text.includes(marker) && text.includes("reconciled nightly"))).toBe(true);
  expect(sent.some((text) => text.includes(`draftonly${marker}`))).toBe(false);
  expect(unpublished.recordId).not.toBe(published.recordId);

  // Archived, it leaves the answers even before the sweep removes its rows.
  await admin.invoke("archive_record", { scope, recordId: published.recordId });
  const after = await admin.invoke("search_similar_records", { scope, queryText: `${marker} ledger reconciliation` });
  expect(after.hits.some((hit) => hit.recordId === published.recordId)).toBe(false);
});

test("a project nobody opened to AI is never sent to the model", async ({ admin, project, workItem }) => {
  const marker = `closed${Date.now().toString(36)}`;
  const record = await draft(admin, project, workItem.workItemId, {
    title: `Closed ${marker}`,
    body: `The ${marker} project stays out of the model.`,
  });
  await publish(admin, admin, project, record.recordId);

  // Several sweep passes.
  await new Promise((resolve) => setTimeout(resolve, 20_000));

  expect((await received()).some((text) => text.includes(marker))).toBe(false);
});
