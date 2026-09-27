import { Api } from "../support/api";
import { invite } from "../support/people";
import { unique } from "../support/env";
import { draft, expect, projectPath, publish, scopeOf, signIn, test } from "../support/fixtures";
import { McpSession } from "../support/mcp";

/**
 * The AI channel, over the MCP server's HTTP transport, as an assistant reaches it.
 *
 * What these prove is the boundary rather than the tools: the surface is the allow-list and
 * nothing more, a project nobody opened to AI is invisible, and whatever an assistant writes is
 * marked as an assistant's in the web client and in the audit trail.
 */

test("the MCP server refuses a caller with no bearer", async () => {
  const response = await McpSession.post(null, {
    jsonrpc: "2.0",
    id: 1,
    method: "initialize",
    params: { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "anonymous", version: "0" } },
  });

  expect(response.status()).toBe(401);
});

const initialize = (name: string) => ({
  jsonrpc: "2.0",
  id: 1,
  method: "initialize",
  params: { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name, version: "0" } },
});

test("the web client's access token is not a credential on the MCP transport", async ({ admin }) => {
  // It was until Phase 14 (A4). It lasts fifteen minutes and reaches every workspace its owner
  // belongs to; a machine token is what an assistant holds.
  expect((await McpSession.post(admin.accessToken, initialize("access-token"))).status()).toBe(401);
});

test("a machine token stops working on the MCP transport as soon as it is revoked", async ({ admin, people }) => {
  const person = await invite(admin, people.workspaceId, "Viewer");
  const api = await Api.signIn(person.email, person.password);

  try {
    const minted = await api.invoke("issue_machine_token", {
      workspaceId: people.workspaceId,
      name: unique("Revoked assistant"),
      lifetimeDays: 1,
    });

    expect((await McpSession.post(minted.token, initialize("revoked"))).status()).toBe(200);

    await api.invoke("revoke_machine_token", { workspaceId: people.workspaceId, tokenId: minted.tokenId });

    // Resolved on every request, so the next one is refused.
    expect((await McpSession.post(minted.token, initialize("revoked"))).status()).toBe(401);
  } finally {
    await api.dispose();
  }
});

test("the tool list is exactly the operations the manifest marks as available to AI", async ({ admin, people }) => {
  const manifest = (await (await admin.get("/operations")).json()) as { name: string; availableToAi: boolean }[];
  const allowed = manifest.filter((entry) => entry.availableToAi).map((entry) => entry.name).sort();
  const humanOnly = manifest.filter((entry) => !entry.availableToAi).map((entry) => entry.name);

  const mcp = await McpSession.as(admin, people.workspaceId);

  try {
    const tools = (await mcp.listTools()).map((tool) => tool.name).sort();

    expect(tools).toEqual(allowed);
    expect(tools).toHaveLength(20);

    for (const name of humanOnly) {
      expect(tools, `${name} must not be a tool`).not.toContain(name);
    }

    // Asking for one anyway is not a way in.
    const attempt = await mcp.callTool("delete_project", {}).catch((error: Error) => ({
      isError: true,
      text: error.message,
      structured: null,
    }));
    expect(attempt.isError).toBe(true);
  } finally {
    await mcp.close();
  }
});

test("a project is invisible to AI until somebody opens it, and closed again when they close it", async ({
  admin,
  project,
  workItem,
}) => {
  const scope = scopeOf(project);
  const mcp = await McpSession.as(admin, scope.workspaceId);

  try {
    const listed = async () =>
      (await mcp.expectTool<{ projects: { projectId: string }[] }>("list_projects", { workspaceId: scope.workspaceId }))
        .projects.map((entry) => entry.projectId);

    expect(await listed()).not.toContain(scope.projectId);

    const closed = await mcp.callTool("get_work_item", { scope, workItemId: workItem.workItemId });
    expect(closed.isError).toBe(true);
    expect(closed.text).not.toContain(workItem.title);

    await admin.invoke("enable_project_ai_access", { scope });
    expect(await listed()).toContain(scope.projectId);

    const open = await mcp.expectTool<{ title: string }>("get_work_item", { scope, workItemId: workItem.workItemId });
    expect(open.title).toBe(workItem.title);

    await admin.invoke("disable_project_ai_access", { scope });
    expect(await listed()).not.toContain(scope.projectId);
    expect((await mcp.callTool("get_work_item", { scope, workItemId: workItem.workItemId })).isError).toBe(true);
  } finally {
    await mcp.close();
  }
});

test("an assistant's draft is marked as AI-written whatever it claims, and audited as an assistant's", async ({
  page,
  admin,
  people,
  project,
  workItem,
}) => {
  const scope = scopeOf(project);
  await admin.invoke("enable_project_ai_access", { scope });

  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const mcp = await McpSession.as(contributor, scope.workspaceId);
  // No long run of digits in anything sent over the AI channel: thirteen of them can pass the Thai
  // national ID checksum, and SB-18 then refuses the draft, correctly.
  const title = unique("Assistant summary of retries");

  try {
    const created = await mcp.expectTool<{ recordId: string; status: string }>("create_draft", {
      scope,
      workItemId: workItem.workItemId,
      kind: "TechnicalKnowledge",
      title,
      body: "Retries reuse the idempotency key of the first attempt.",
      frontMatter: {},
      provenance: {
        // A claim the server must not believe on this channel.
        sourceKind: "HumanAuthored",
        sourceLocator: "src/payments.ts",
        author: "An assistant",
        recordedAt: new Date().toISOString(),
        evidence: [],
      },
    });
    expect(created.status).toBe("Draft");

    // Drafting is as far as an assistant goes.
    const submit = await mcp.callTool("submit_for_approval", { scope, recordId: created.recordId });
    expect(submit.isError).toBe(true);

    await signIn(page, people.reviewer, projectPath(project, `/records/${created.recordId}`));
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(title);
    await expect(page.getByText(/· drafted by AI ·/)).toBeVisible();
  } finally {
    await mcp.close();
    await contributor.dispose();
  }

  const audit = await admin.invoke("read_audit_history", {
    scope,
    occurredFrom: new Date(Date.now() - 60 * 60 * 1000).toISOString(),
    occurredUntil: new Date(Date.now() + 60 * 1000).toISOString(),
    channel: "Ai",
  });
  const drafted = audit.entries.filter((entry) => entry.action === "DraftCreated");
  expect(drafted).toHaveLength(1);
  expect(drafted[0]!.channel).toBe("Ai");
  expect(drafted[0]!.actorId).toBe(people.contributor.userId);
});

test("an assistant reads only published knowledge, and a viewer's assistant cannot write", async ({
  admin,
  people,
  project,
  workItem,
}) => {
  const scope = scopeOf(project);
  await admin.invoke("enable_project_ai_access", { scope });

  const contributor = await Api.signIn(people.contributor.email, people.contributor.password);
  const reviewer = await Api.signIn(people.reviewer.email, people.reviewer.password);
  const viewer = await Api.signIn(people.viewer.email, people.viewer.password);
  const word = `quokka${Date.now().toString(36)}`;

  const published = await draft(contributor, scope, workItem.workItemId, {
    title: `Published ${word}`,
    body: `The ${word} job runs nightly.`,
  });
  await publish(contributor, reviewer, scope, published.recordId);
  const unpublished = await draft(contributor, scope, workItem.workItemId, {
    title: `Unpublished ${word}`,
    body: `The ${word} job might run hourly.`,
  });

  const mcp = await McpSession.as(viewer, scope.workspaceId);

  try {
    const hits = await mcp.expectTool<{ hits: { title: string }[] }>("search_knowledge", {
      scope,
      queryText: word,
      statuses: ["Published"],
    });
    expect(hits.hits.map((hit) => hit.title)).toEqual([`Published ${word}`]);

    const record = await mcp.expectTool<{ body: string }>("get_record", { scope, recordId: published.recordId });
    expect(record.body).toContain("runs nightly");

    // Never published, so there is nothing for a reader to be given.
    expect((await mcp.callTool("get_record", { scope, recordId: unpublished.recordId })).isError).toBe(true);

    // The tool exists; the viewer's role does not carry it.
    const write = await mcp.callTool("create_draft", {
      scope,
      workItemId: workItem.workItemId,
      kind: "Decision",
      title: "A viewer's assistant tries to write",
      body: "This must be refused.",
      frontMatter: {},
      provenance: {
        sourceKind: "AiDraft",
        sourceLocator: "chat",
        author: "An assistant",
        recordedAt: new Date().toISOString(),
        evidence: [],
      },
    });
    expect(write.isError).toBe(true);

    const semantic = await mcp.expectTool<{ hits: unknown[]; unavailable?: string | null }>("search_similar_records", {
      scope,
      queryText: "nightly jobs",
    });
    expect(semantic.hits).toHaveLength(0);
    expect(semantic.unavailable).toBeTruthy();
  } finally {
    await mcp.close();
    await contributor.dispose();
    await reviewer.dispose();
    await viewer.dispose();
  }
});
