import { existsSync } from "node:fs";
import { resolve } from "node:path";
import type { ClientContext } from "./context.js";
import { credentialKey } from "./file-store.js";
import { checkoutRoot, guid, origin, Registry } from "./registry.js";
import type { Checkout } from "./registry.js";
import { verify } from "./http.js";
import { Cancelled, readToken, validToken } from "./token-input.js";
import { bridge } from "./bridge.js";
import { readFileSync } from "node:fs";
import { ClientError } from "./errors.js";

export interface CommandDependencies {
  verify: typeof verify;
  readToken(): Promise<string | null>;
}
const defaults: CommandDependencies = { verify, readToken };
const helpOptions = new Set(["--help", "-h", "-?"]);
const specs: Record<string, { values: string[]; flags: string[]; path?: boolean; required?: string[] }> = {
  register: { values: ["server", "workspace", "project", "label"], flags: [], path: true, required: ["server", "workspace"] },
  list: { values: [], flags: [] },
  show: { values: [], flags: ["json"], path: true },
  update: { values: ["server", "workspace", "project", "label"], flags: ["clear-project"], path: true },
  unregister: { values: [], flags: [], path: true },
  "token set": { values: ["server", "workspace"], flags: [], required: ["server", "workspace"] },
  "token remove": { values: ["server", "workspace"], flags: ["force"], required: ["server", "workspace"] },
  doctor: { values: [], flags: [], path: true },
  "mcp-headers": { values: ["dir", "url"], flags: [] },
  "mcp-bridge": { values: ["server", "workspace", "project", "dir", "log"], flags: [] },
};

function describe(context: ClientContext, entry: Checkout): void {
  context.out(`Server:    ${entry.server}\nWorkspace: ${entry.workspace}\nProject:   ${entry.project ?? "(none)"}\nLabel:     ${entry.label ?? "(none)"}`);
}

export async function run(context: ClientContext, args: string[], dependencies: CommandDependencies = defaults): Promise<number> {
  try {
    if (args.length === 1 && args[0] === "--version") {
      context.out(JSON.parse(readFileSync(new URL("../package.json", import.meta.url), "utf8")).version); return 0;
    }
    if (args.length === 0 || helpOptions.has(args[0]!)) {
      context.out("DevBuddy npm client\nCommands: " + Object.keys(specs).join(", ") + "\nUse <command> --help. Copy registration details from Plugin access. Tokens are read without echo; never pass them as arguments."); return 0;
    }
    let command = args[0]!;
    let remaining = args.slice(1);
    if (command === "token") {
      if (remaining.some(argument => helpOptions.has(argument)) && (remaining.length === 1 || helpOptions.has(remaining[0]!))) {
        context.out("devbuddy-npx token <set|remove> --server <origin> --workspace <GUID>\nUse token set --help or token remove --help. Tokens are read without echo."); return 0;
      }
      command += " " + remaining[0]; remaining = remaining.slice(1);
    }
    const spec = specs[command];
    if (!spec) throw new ClientError("Unknown command. Use --help.");
    const optionEnd = remaining.indexOf("--");
    if (remaining.slice(0, optionEnd < 0 ? remaining.length : optionEnd).some(argument => helpOptions.has(argument))) {
      context.out(`devbuddy-npx ${command}${spec.path ? " [path]" : ""} ${spec.values.map(name => `${spec.required?.includes(name) ? "" : "["}--${name} <value>${spec.required?.includes(name) ? "" : "]"}`).join(" ")} ${spec.flags.map(name => `[--${name}]`).join(" ")}\nPaths default to the current folder. Server and workspace identify the token; see Plugin access.`); return 0;
    }
    const values: Record<string, string> = {};
    const flags = new Set<string>();
    let path: string | undefined;
    let positionalOnly = false;
    for (let index = 0; index < remaining.length; index++) {
      const arg = remaining[index]!;
      if (!positionalOnly && arg === "--") { positionalOnly = true; continue; }
      if (!positionalOnly && arg.startsWith("--")) {
        const [name, ...assigned] = arg.slice(2).split("=");
        if (spec.flags.includes(name!)) {
          let enabled = true;
          if (assigned.length) {
            if (assigned.length !== 1 || !/^(?:true|false)$/i.test(assigned[0]!)) throw new ClientError("Expected true or false for a boolean option.");
            enabled = assigned[0]!.toLowerCase() === "true";
          } else if (/^(?:true|false)$/i.test(remaining[index + 1] ?? "")) {
            enabled = remaining[++index]!.toLowerCase() === "true";
          }
          if (enabled) flags.add(name!); else flags.delete(name!);
          continue;
        }
        if (!spec.values.includes(name!)) throw new ClientError("Unknown option. Use command --help.");
        const value = assigned.length ? assigned.join("=") : remaining[++index];
        if (value === undefined || value.startsWith("--") || values[name!] !== undefined) throw new ClientError("Missing or repeated option value.");
        values[name!] = value;
      } else {
        if (!spec.path || path !== undefined) throw new ClientError("Unexpected argument. Use command --help.");
        path = arg;
      }
    }
    if (spec.required?.some(name => values[name] === undefined)) throw new ClientError("Both --server and --workspace are required.");
    // Validate all identifiers before reading a store or contacting any server.
    if (command === "mcp-bridge") {
      const cancellation = new AbortController();
      const cancel = (): void => cancellation.abort();
      process.once("SIGINT", cancel);
      try { await bridge(context, values, process.stdin, process.stdout, cancellation.signal); }
      finally { process.off("SIGINT", cancel); }
      return cancellation.signal.aborted ? 130 : 0;
    }
    if (values.server !== undefined) values.server = origin(values.server);
    if (values.workspace !== undefined) values.workspace = guid(values.workspace);
    if (values.project !== undefined) values.project = guid(values.project);
    const folder = resolve(context.cwd, path ?? values.dir ?? ".");
    const registry = Registry.load(context.home);
    const entry = registry.find(folder);
    async function tokenFor(server: string, workspace: string, replace = false): Promise<string> {
      const store = await context.store();
      const key = credentialKey(server, workspace);
      let token = replace ? null : store.read(key);
      const supplied = token === null;
      if (supplied) token = await dependencies.readToken();
      if (!validToken(token)) throw new ClientError("Expected a 43-character base64url machine token.");
      await dependencies.verify(server, token, workspace);
      if (supplied) { store.write(key, token); context.out(`Token stored in ${store.description}.`); }
      return token;
    }
    if (command === "list") {
      if (!registry.all.length) context.out("No checkout is registered. Copy the register command from Plugin access.");
      for (const item of registry.all) context.out(`${item.path}${existsSync(item.path) ? "" : "  (folder missing)"}\n    ${item.server}  workspace ${item.workspace}${item.label ? ` (${item.label})` : ""}${item.project ? `  project ${item.project}` : ""}`);
      return 0;
    }
    if (command === "register") {
      const root = checkoutRoot(folder, context.userHome);
      await tokenFor(values.server!, values.workspace!);
      const registered: Checkout = { path: root, server: values.server!, workspace: values.workspace!,
        ...(values.project ? { project: values.project } : {}), ...(values.label?.trim() ? { label: values.label.trim() } : {}) };
      registry.put(registered); registry.save(); context.out(`Registered ${root}`); describe(context, registered); return 0;
    }
    if (command === "token set") { await tokenFor(values.server!, values.workspace!, true); return 0; }
    if (command === "token remove") {
      if (registry.usingToken(values.server!, values.workspace!).length && !flags.has("force")) throw new ClientError("Checkouts still use this token. Unregister them first, or pass --force.");
      const removed = (await context.store()).delete(credentialKey(values.server!, values.workspace!));
      context.out(removed ? "Removed." : "No token was stored for that server and workspace.");
      context.out("Revoke it under Plugin access too, if it should stop working everywhere."); return 0;
    }
    if (!entry) { context.error("DevBuddy: this folder and its parents are not registered. Copy the register command from Plugin access."); return 2; }
    if (command === "show") {
      if (flags.has("json")) {
        context.out(JSON.stringify({ path: entry.path, server: entry.server, mcpUrl: `${entry.server}/mcp`, workspaceId: entry.workspace, projectId: entry.project ?? null,
          note: "This is a registration, not DevBuddy content. Ask DevBuddy through its MCP tools." }, null, 2)); return 0;
      }
      describe(context, entry);
      try { const store = await context.store(); context.out(store.read(credentialKey(entry.server, entry.workspace)) ? `Token:     stored in ${store.description}` : "Token:     none stored. Run devbuddy-npx token set."); }
      catch { context.out("Token:     could not be checked here. The credential store could not be read."); }
      return 0;
    }
    if (command === "unregister") {
      registry.remove(entry.path); registry.save(); context.out(`Unregistered ${entry.path}.`);
      context.out("The workspace token stays stored for other checkouts. Use token remove to remove it."); return 0;
    }
    if (command === "update") {
      const server = values.server ?? entry.server, workspace = values.workspace ?? entry.workspace;
      if (server !== entry.server || workspace !== entry.workspace) await tokenFor(server, workspace);
      const updated: Checkout = { path: entry.path, server, workspace };
      const project = flags.has("clear-project") ? undefined : values.project ?? entry.project;
      const label = values.label === undefined ? entry.label : values.label.trim();
      if (project) updated.project = project;
      if (label) updated.label = label;
      registry.put(updated); registry.save(); context.out(`Updated ${updated.path}`); describe(context, updated); return 0;
    }
    if (command === "mcp-headers") {
      const target = values.url || context.environment.CLAUDE_CODE_MCP_SERVER_URL;
      let matching = false;
      try { matching = !!target && origin(target) === entry.server; } catch { /* Fail closed before opening a store. */ }
      if (!matching) { context.error("DevBuddy: the helper URL does not match the registered server. No token was sent."); return 3; }
    }
    let token: string | null;
    try { token = (await context.store()).read(credentialKey(entry.server, entry.workspace)); }
    catch { context.error("DevBuddy: the credential store could not be read. No token was sent."); return 4; }
    if (!validToken(token)) { context.error("DevBuddy: no valid token is stored. Run devbuddy-npx token set."); return 4; }
    if (command === "mcp-headers") { context.out(JSON.stringify({ Authorization: `Bearer ${token}` })); return 0; }
    await dependencies.verify(entry.server, token, entry.workspace);
    context.out(`[ok] registered: ${entry.path}\n[ok] token accepted in this workspace\n[ok] MCP endpoint ${entry.server}/mcp`); return 0;
  } catch (error) {
    if (error instanceof Cancelled) return 130;
    // Only static client messages are exposed; OS/network/server exceptions can carry secrets.
    context.error(error instanceof ClientError ? `DevBuddy: ${error.message}` : "DevBuddy: the command failed. Check arguments, registry, credential access and server connectivity.");
    return 1;
  }
}
