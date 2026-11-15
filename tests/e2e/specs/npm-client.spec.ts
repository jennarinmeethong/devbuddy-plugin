import { randomUUID } from "node:crypto";
import { Api } from "../support/api";
import { unique } from "../support/env";
import { expect, scopeOf, test } from "../support/fixtures";
import { NpxClient, type Reply } from "../support/npx-client";
import { invite } from "../support/people";

function toolData<T>(reply: Reply): T {
  expect(reply.error).toBeUndefined();
  expect(reply.result?.isError).not.toBe(true);
  const result = reply.result!;
  return (result.structuredContent ?? JSON.parse((result.content ?? []).map(part => part.text ?? "").join("\n"))) as T;
}

test("npm client registers, bridges the real catalog and adopts token rotation/revocation", async ({ admin, people, project, workItem }) => {
  const client = new NpxClient();
  const tokens: string[] = [];
  let bridge: ReturnType<NpxClient["bridge"]> | undefined;
  try {
    await client.start();
    await admin.invoke("enable_project_ai_access", { scope: scopeOf(project) });
    const minted = await admin.invoke("issue_machine_token", { workspaceId: people.workspaceId, name: unique("npm client"), lifetimeDays: 1 });
    tokens.push(minted.tokenId);
    const checkout = client.checkout("Customer checkout");
    const registered = await client.command(["register", checkout, "--server", client.origin, "--workspace", people.workspaceId, "--project", project.projectId], minted.token + "\n");
    expect(registered.code, registered.stderr).toBe(0);
    const shown = await client.command(["show", checkout, "--json"]);
    expect(JSON.parse(shown.stdout).workspaceId).toBe(people.workspaceId);
    const helper = await client.command(["mcp-headers", "--dir", checkout, "--url", client.origin + "/mcp"]);
    expect(helper.code).toBe(0);
    expect(JSON.parse(helper.stdout).Authorization === `Bearer ${minted.token}`).toBe(true);
    const wrongOrigin = await client.command(["mcp-headers", "--dir", checkout, "--url", "https://wrong.invalid/mcp"]);
    expect(wrongOrigin.code).toBe(3); expect(wrongOrigin.stdout).toBe("");
    const wrongWorkspace = await client.command(["token", "set", "--server", client.origin, "--workspace", randomUUID()], minted.token + "\n");
    expect(wrongWorkspace.code).toBe(1);
    expect((await client.command(["doctor", checkout])).code).toBe(0);

    bridge = client.bridge(checkout);
    const initialized = await bridge.initialize();
    expect(initialized.error).toBeUndefined();
    expect(initialized.result?.instructions).toContain(people.workspaceId);
    const listed = await bridge.rpc("tools/list");
    const manifest = await (await admin.get("/operations")).json() as { name: string; availableToAi: boolean }[];
    expect(listed.result?.tools?.map(tool => tool.name).sort()).toEqual(manifest.filter(operation => operation.availableToAi).map(operation => operation.name).sort());
    const args = { scope: { workspaceId: randomUUID(), projectId: project.projectId }, workItemId: workItem.workItemId };
    expect(toolData<{ title: string }>(await bridge.call("get_work_item", args)).title).toBe(workItem.title);

    const replacement = await admin.invoke("issue_machine_token", { workspaceId: people.workspaceId, name: unique("npm replacement"), lifetimeDays: 1 });
    tokens.push(replacement.tokenId);
    expect((await client.command(["token", "set", "--server", client.origin, "--workspace", people.workspaceId], replacement.token + "\n")).code).toBe(0);
    await admin.invoke("revoke_machine_token", { workspaceId: people.workspaceId, tokenId: minted.tokenId });
    expect(toolData<{ title: string }>(await bridge.call("get_work_item", args)).title).toBe(workItem.title);
    await admin.invoke("revoke_machine_token", { workspaceId: people.workspaceId, tokenId: replacement.tokenId });
    expect((await bridge.call("get_work_item", args)).error).toBeDefined();
    expect((await client.command(["doctor", checkout])).code).toBe(1);
    expect((await client.command(["unregister", checkout])).code).toBe(0);
    expect((await client.command(["token", "remove", "--server", client.origin, "--workspace", people.workspaceId])).code).toBe(0);
    const after = await (await admin.get("/operations")).json();
    expect(after).toEqual(manifest);
  } finally {
    if (bridge) await bridge.close();
    await Promise.allSettled(tokens.map(tokenId => admin.invoke("revoke_machine_token", { workspaceId: people.workspaceId, tokenId })));
    await client.dispose();
  }
});

test("npm bridge requires a workspace choice and keeps the real session in that workspace", async ({ admin, people }) => {
  const founder = await invite(admin, people.workspaceId, "Administrator");
  const owner = await Api.signIn(founder.email, founder.password);
  const client = new NpxClient();
  const tokens: { workspaceId: string; tokenId: string }[] = [];
  let bridge: ReturnType<NpxClient["bridge"]> | undefined;
  try {
    await client.start();
    const other = await owner.invoke("create_workspace", { sponsorWorkspaceId: people.workspaceId, name: unique("npm other"), firstProjectName: "npm isolated project" });
    expect(other.projectId).not.toBeNull();
    await owner.invoke("enable_project_ai_access", { scope: { workspaceId: other.workspaceId, projectId: other.projectId! } });
    for (const [index, workspaceId] of [people.workspaceId, other.workspaceId].entries()) {
      const minted = await owner.invoke("issue_machine_token", { workspaceId, name: unique("npm workspace"), lifetimeDays: 1 });
      tokens.push({ workspaceId, tokenId: minted.tokenId });
      expect((await client.command(["register", client.checkout(`workspace-${index}`), "--server", client.origin, "--workspace", workspaceId, "--label", "Customer"], minted.token + "\n")).code).toBe(0);
    }
    bridge = client.bridge();
    expect((await bridge.initialize()).error).toBeUndefined();
    expect((await bridge.rpc("tools/list")).result?.tools?.some(tool => tool.name === "use_workspace")).toBe(true);
    expect((await bridge.call("list_projects", {})).result?.isError).toBe(true);
    expect((await bridge.call("use_workspace", { workspace: "Customer" })).result?.isError).toBe(true);
    expect((await bridge.call("use_workspace", { workspace: other.workspaceId })).result?.isError).toBe(false);
    const selected = toolData<{ projects: { projectId: string }[] }>(await bridge.call("list_projects", { workspaceId: people.workspaceId }));
    expect(selected.projects.map(project => project.projectId)).toContain(other.projectId);
    expect((await bridge.call("use_workspace", { workspace: people.workspaceId })).result?.isError).toBe(true);
    expect((await client.command(["token", "remove", "--server", client.origin, "--workspace", other.workspaceId, "--force"])).code).toBe(0);
    expect((await bridge.call("list_projects", {})).error).toBeDefined();
  } finally {
    if (bridge) await bridge.close();
    await Promise.allSettled(tokens.map(token => owner.invoke("revoke_machine_token", token)));
    await owner.dispose(); await client.dispose();
  }
});
