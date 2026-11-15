import { createInterface } from "node:readline";
import type { Readable, Writable } from "node:stream";
import { appendFileSync } from "node:fs";
import { basename, resolve } from "node:path";
import type { ClientContext } from "./context.js";
import { credentialKey } from "./file-store.js";
import { Registry, guid, origin } from "./registry.js";
import type { Checkout } from "./registry.js";
import { answers, post, statusProblem } from "./http.js";
import type { Rpc, Transport } from "./http.js";
import { validToken } from "./token-input.js";
import { ClientError } from "./errors.js";

export interface BridgeOptions { server?: string; workspace?: string; project?: string; dir?: string; log?: string }
const given = (value: string | undefined): string | undefined => value?.trim() && !value.includes("${") ? value.trim() : undefined;
const result = (id: unknown, value: Rpc): Rpc => ({ jsonrpc: "2.0", id, result: value });
const failure = (id: unknown, message: string, code = -32000): Rpc => ({ jsonrpc: "2.0", id, error: { code, message } });
const toolResult = (id: unknown, text: string, isError = false): Rpc => result(id, { content: [{ type: "text", text }], isError });

export function bridgeTargets(context: ClientContext, options: BridgeOptions): Checkout[] {
  const server = given(options.server), workspace = given(options.workspace), project = given(options.project);
  if (server || workspace) {
    if (!server || !workspace) throw new Error("The bridge requires --server and --workspace together.");
    return [{ path: "(given)", server: origin(server), workspace: guid(workspace), ...(project ? { project: guid(project) } : {}) }];
  }
  const registry = Registry.load(context.home);
  const entry = registry.find(resolve(context.cwd, given(options.dir) ?? "."));
  if (entry) return [entry];
  const groups = new Map<string, Checkout[]>();
  for (const item of registry.all) {
    const key = credentialKey(item.server, item.workspace);
    groups.set(key, [...groups.get(key) ?? [], item]);
  }
  return [...groups.values()].map(entries => {
    const first = entries[0]!;
    const projects = new Set(entries.flatMap(item => item.project ? [item.project] : []));
    const choice: Checkout = { path: first.path, server: first.server, workspace: first.workspace,
      label: entries.find(item => item.label)?.label ?? [...new Set(entries.map(item => basename(item.path)))].join(", ") };
    if (projects.size === 1) choice.project = [...projects][0]!;
    return choice;
  }).sort((a, b) => (a.label ?? "").localeCompare(b.label ?? ""));
}

export async function bridge(context: ClientContext, options: BridgeOptions, input: Readable, output: Writable,
  signal: AbortSignal, transport: Transport = post): Promise<void> {
  let choices: Checkout[] = [], problem: string | undefined;
  try { choices = bridgeTargets(context, options); } catch { problem = "The bridge configuration or registry is invalid. Check server/workspace identifiers and registration."; }
  if (!choices.length) problem ??= "Nothing is registered. Copy the register command from Plugin access and run it in your working folder.";
  let chosen = choices.length === 1 ? choices[0] : undefined;
  let protocol: string | undefined;
  const topLevel = new Set<string>(), scoped = new Set<string>();
  let writing = Promise.resolve();
  const write = (message: Rpc): Promise<void> => {
    writing = writing.then(() => new Promise<void>((resolveWrite, reject) => {
      output.write(JSON.stringify(message) + "\n", error => error ? reject(error) : resolveWrite());
    }));
    return writing;
  };
  function log(method: string, status?: number): void {
    if (options.log) appendFileSync(options.log, JSON.stringify({ timestamp: new Date().toISOString(),
      method: ["initialize", "tools/list", "tools/call", "notifications/initialized", "ping"].includes(method) ? method : "other",
      ...(status === undefined ? {} : { status }) }) + "\n", { mode: 0o600 });
  }
  const guidance = (): string => choices.length > 1
    ? `Ask the person which workspace this task is for, then call use_workspace. Choices: ${choices.map(item => `${item.label ?? item.workspace} (${item.workspace}) on ${item.server}`).join("; ")}. The choice holds until the task ends. Do not choose for them.`
    : `This connection works only in workspace ${chosen?.workspace}. ${chosen?.project ? `Default project: ${chosen.project}.` : "Call list_projects to find a project."} The bridge fills the workspace on every call. This is a registration, not DevBuddy content.`;
  const chooseTool = (): Rpc => ({ name: "use_workspace", description: problem ?? guidance(), inputSchema: {
    type: "object", properties: { workspace: { type: "string", description: "The chosen workspace name or identifier." } }, required: ["workspace"],
  } });
  function remove(schema: Rpc, property: string): boolean {
    if (!schema?.properties || !Object.hasOwn(schema.properties, property)) return false;
    delete schema.properties[property];
    if (Array.isArray(schema.required)) schema.required = schema.required.filter((item: unknown) => item !== property);
    return true;
  }
  async function handle(line: string): Promise<void> {
    let message: Rpc;
    try { message = JSON.parse(line); } catch { await write(failure(null, "Invalid JSON-RPC message.", -32700)); return; }
    if (!message || typeof message !== "object" || Array.isArray(message) || message.jsonrpc !== "2.0" || typeof message.method !== "string"
      || (Object.hasOwn(message, "id") && typeof message.id !== "string" && typeof message.id !== "number" && message.id !== null)
      || (message.params !== undefined && (!message.params || typeof message.params !== "object" || Array.isArray(message.params)))) {
      await write(failure(null, "Invalid JSON-RPC request.", -32600)); return;
    }
    const id = message.id, method = message.method;
    const expectsAnswer = Object.hasOwn(message, "id");
    const tool = method === "tools/call" ? message.params?.name : undefined;
    if (method === "tools/call" && (typeof tool !== "string" || (message.params?.arguments !== undefined && (!message.params.arguments || typeof message.params.arguments !== "object" || Array.isArray(message.params.arguments))))) {
      if (expectsAnswer) await write(failure(id, "Invalid tool arguments.", -32602)); return;
    }
    if (problem) {
      if (!expectsAnswer) return;
      if (method === "initialize") await write(result(id, { protocolVersion: message.params?.protocolVersion ?? "2025-06-18", capabilities: { tools: {} }, serverInfo: { name: "devbuddy-npx", version: "bridge" }, instructions: problem }));
      else if (method === "tools/list") await write(result(id, { tools: [chooseTool()] }));
      else if (method === "tools/call") await write(toolResult(id, problem, true));
      else await write(result(id, {}));
      return;
    }
    if (tool === "use_workspace") {
      const wanted = message.params?.arguments?.workspace;
      const matches = typeof wanted === "string" ? choices.filter(item => item.workspace.toLowerCase() === wanted.toLowerCase() || item.label?.toLowerCase() === wanted.toLowerCase()) : [];
      let reason: string | undefined;
      const match = matches[0];
      if (matches.length !== 1 || !match) reason = "The workspace name is missing or ambiguous. Ask the person for its identifier.";
      else if (chosen && credentialKey(chosen.server, chosen.workspace) !== credentialKey(match.server, match.workspace)) reason = "This task already chose another workspace. Start a new task to change it.";
      else {
        try { if (!validToken((await context.store()).read(credentialKey(match.server, match.workspace)))) reason = "No token is stored for that workspace. Run token set."; }
        catch { reason = "The credential store could not be read."; }
        // Re-check after awaiting a store: concurrent choices may have settled meanwhile.
        if (!reason && chosen && credentialKey(chosen.server, chosen.workspace) !== credentialKey(match.server, match.workspace)) reason = "This task already chose another workspace. Start a new task to change it.";
        if (!reason) chosen = match;
      }
      if (expectsAnswer) await write(toolResult(id, reason ?? `This task now uses workspace ${chosen!.workspace}. ${chosen!.project ? `Default project: ${chosen!.project}.` : "Call list_projects."}`, !!reason));
      return;
    }
    if (tool && !chosen) { if (expectsAnswer) await write(toolResult(id, guidance(), true)); return; }
    const target = chosen ?? choices[0]!;
    if (tool) {
      const args = message.params.arguments ??= {};
      // Replace both supplied scopes even if only one is part of the schema.
      if (Object.hasOwn(args, "workspaceId")) args.workspaceId = target.workspace;
      if (args.scope && typeof args.scope === "object" && !Array.isArray(args.scope)) args.scope.workspaceId = target.workspace;
      if (scoped.has(tool) || (!topLevel.has(tool) && args.scope && typeof args.scope === "object" && !Array.isArray(args.scope))) {
        if (!args.scope || typeof args.scope !== "object" || Array.isArray(args.scope)) args.scope = {};
        args.scope.workspaceId = target.workspace;
      } else args.workspaceId = target.workspace;
    }
    try {
      const token = (await context.store()).read(credentialKey(target.server, target.workspace));
      if (!validToken(token)) throw new Error();
      const response = await transport(target.server, token, message, protocol, AbortSignal.any([signal, AbortSignal.timeout(300_000)]));
      log(method, response.status);
      if (!response.ok) { if (expectsAnswer) await write(failure(id, statusProblem(response.status))); return; }
      for await (const answer of answers(response)) {
        if (method === "initialize" && answer.id === id && answer.result) {
          if (typeof answer.result.protocolVersion === "string") protocol = answer.result.protocolVersion;
          answer.result.instructions = [typeof answer.result.instructions === "string" ? answer.result.instructions : "", guidance()].filter(Boolean).join("\n\n");
        }
        if (method === "tools/list" && Array.isArray(answer.result?.tools)) {
          for (const definition of answer.result.tools) {
            if (typeof definition.name !== "string" || !definition.inputSchema) continue;
            if (remove(definition.inputSchema, "workspaceId")) topLevel.add(definition.name);
            if (remove(definition.inputSchema.properties?.scope, "workspaceId")) scoped.add(definition.name);
          }
          if (choices.length > 1) answer.result.tools.unshift(chooseTool());
        }
        await write(answer);
      }
    } catch (error) {
      if (expectsAnswer && !signal.aborted) await write(failure(id, error instanceof ClientError ? error.message : "Credential access, TLS, network or MCP response failed. Check token set and doctor. The request was not retried."));
    }
  }
  const reader = createInterface({ input, crlfDelay: Infinity });
  const cancelled = (): void => { reader.close(); };
  signal.addEventListener("abort", cancelled, { once: true });
  const pending = new Set<Promise<void>>();
  try {
    for await (const line of reader) {
      if (!line.trim() || signal.aborted) continue;
      const work = handle(line);
      // Parsing the method for the barrier never determines routing or credential choice.
      let initialize = false;
      try { initialize = JSON.parse(line)?.method === "initialize"; } catch { /* handled above */ }
      if (initialize) await work;
      else { pending.add(work); void work.then(() => pending.delete(work), () => pending.delete(work)); }
    }
    await Promise.all(pending);
    await writing;
  } finally { signal.removeEventListener("abort", cancelled); reader.close(); }
}
