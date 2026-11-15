import { afterEach, expect, test } from "bun:test";
import { mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join, parse } from "node:path";
import { createHash } from "node:crypto";
import { PassThrough, Readable, Writable } from "node:stream";
import type { ClientContext } from "../src/context.ts";
import { run } from "../src/cli.ts";
import { Registry, atomicWrite, checkoutRoot, contains, guid, origin } from "../src/registry.ts";
import { credentialKey, fileCredentialStore } from "../src/file-store.ts";
import { answers, post, verify } from "../src/http.ts";
import type { Rpc, Transport } from "../src/http.ts";
import { bridge, bridgeTargets } from "../src/bridge.ts";
import { readToken } from "../src/token-input.ts";

const workspace = "01234567-89ab-cdef-0123-456789abcdef";
const other = "11234567-89ab-cdef-0123-456789abcdef";
const server = "https://example.invalid";
const token = "a".repeat(43);
const homes: string[] = [];
afterEach(() => { for (const home of homes.splice(0)) rmSync(home, { recursive: true, force: true }); });
function harness() {
  const scratch = fileURLToPath(new URL("../out/", import.meta.url)); mkdirSync(scratch, { recursive: true });
  const home = mkdtempSync(join(scratch, "devbuddy-client-")); homes.push(home);
  const cwd = join(home, "งาน ทดสอบ"); mkdirSync(cwd);
  mkdirSync(join(cwd, ".git"));
  const output: string[] = [], errors: string[] = [];
  let accesses = 0;
  const context: ClientContext = { home, userHome: join(home, "user"), cwd, environment: {},
    out: value => { output.push(value); }, error: value => { errors.push(value); },
    async store() { accesses++; return fileCredentialStore(home); } };
  const registry = Registry.load(home);
  registry.put({ path: cwd, server, workspace }); registry.save();
  return { context, output, errors, registry, accesses: () => accesses };
}
const dependencies = { verify: async () => {}, readToken: async () => token };
const json = (value: unknown): Response => new Response(JSON.stringify(value), { headers: { "content-type": "application/json" } });

test("readonly JSON and list never instantiate a credential store", async () => {
  const h = harness();
  h.context.store = async () => { throw new Error("sandbox refused"); };
  expect(await run(h.context, ["show", "--json"])).toBe(0);
  expect(JSON.parse(h.output[0]!)).toEqual({ path: h.context.cwd, server, mcpUrl: server + "/mcp", workspaceId: workspace,
    projectId: null, note: "This is a registration, not DevBuddy content. Ask DevBuddy through its MCP tools." });
  expect(await run(h.context, ["list"])).toBe(0);
  expect(await run(h.context, ["show"])).toBe(0);
  expect(h.output.join("\n")).toContain("could not be checked");
});

test("helper origin is checked before credentials and failure leaves stdout empty", async () => {
  const h = harness();
  expect(await run(h.context, ["mcp-headers", "--url", "https://wrong.invalid/mcp"])).toBe(3);
  expect(h.accesses()).toBe(0); expect(h.output).toEqual([]);
  expect(await run(h.context, ["mcp-headers", "--url", server + "/mcp"])).toBe(4);
  fileCredentialStore(h.context.home).write(credentialKey(server, workspace), token);
  h.context.environment.CLAUDE_CODE_MCP_SERVER_URL = server + "/mcp";
  expect(await run(h.context, ["mcp-headers"])).toBe(0);
  expect(JSON.parse(h.output[0]!)).toEqual({ Authorization: `Bearer ${token}` });
});

test("invalid input performs no credential or network work and does not echo arguments", async () => {
  const h = harness(); let requests = 0;
  for (const args of [["list", "--json"], ["token", "set", "--server", server, "--workspace", token], ["show", "--token", token], ["register", "--server", "http://remote.invalid", "--workspace", workspace]]) {
    expect(await run(h.context, args, { ...dependencies, verify: async () => { requests++; } })).toBe(1);
  }
  expect(requests).toBe(0); expect(h.accesses()).toBe(0);
  expect(h.output.join("") + h.errors.join("")).not.toContain(token);
});

test("token rejection preserves previous token and registry", async () => {
  const h = harness(), store = fileCredentialStore(h.context.home), key = credentialKey(server, workspace);
  store.write(key, token);
  const before = readFileSync(h.registry.file, "utf8");
  const reject = { readToken: async () => "b".repeat(43), verify: async () => { throw new Error(token); } };
  expect(await run(h.context, ["token", "set", "--server", server, "--workspace", workspace], reject)).toBe(1);
  expect(store.read(key)).toBe(token); expect(readFileSync(h.registry.file, "utf8")).toBe(before);
  expect(h.errors.join("")).not.toContain(token);
});

test("registration reuses stored token, updates metadata and unregister keeps token", async () => {
  const h = harness(), store = fileCredentialStore(h.context.home), key = credentialKey(server, workspace);
  store.write(key, token);
  const deps = { verify: async () => {}, readToken: async () => { throw new Error("must not prompt"); } };
  expect(await run(h.context, ["register", "--server", server, "--workspace", workspace, "--label", "  Customer  "], deps)).toBe(0);
  expect(Registry.load(h.context.home).find(h.context.cwd)?.label).toBe("Customer");
  expect(await run(h.context, ["update", "--project", other, "--clear-project", "--label", ""])).toBe(0);
  expect(Registry.load(h.context.home).find(h.context.cwd)?.project).toBeUndefined();
  expect(Registry.load(h.context.home).find(h.context.cwd)?.label).toBeUndefined();
  expect(await run(h.context, ["token", "remove", "--server", server, "--workspace", workspace])).toBe(1);
  expect(await run(h.context, ["unregister"])).toBe(0); expect(store.read(key)).toBe(token);
  expect(await run(h.context, ["token", "remove", "--server", server, "--workspace", workspace])).toBe(0);
  expect(store.read(key)).toBeNull();
});

test("registry rejects corrupt and future schemas without replacing files", () => {
  const h = harness();
  for (const value of ["{", '{"version":2,"checkouts":[]}', '{"version":1,"checkouts":[{}]}']) {
    writeFileSync(h.registry.file, value);
    expect(() => Registry.load(h.context.home)).toThrow("registry is invalid");
    expect(readFileSync(h.registry.file, "utf8")).toBe(value);
  }
});

test("failed atomic replacement preserves destination and removes its unique temporary file", () => {
  const h = harness(); const destination = join(h.context.home, "destination"); mkdirSync(destination);
  writeFileSync(join(destination, "original"), "keep");
  expect(() => atomicWrite(destination, "replacement")).toThrow();
  expect(readFileSync(join(destination, "original"), "utf8")).toBe("keep");
  expect(readdirSync(h.context.home).filter(file => file.endsWith(".tmp"))).toEqual([]);
});

test("every command offers help without opening registry or credential store", async () => {
  const h = harness(); writeFileSync(h.registry.file, "invalid JSON");
  for (const command of ["register", "list", "show", "update", "unregister", "doctor", "mcp-headers", "mcp-bridge", "token set", "token remove"]) {
    expect(await run(h.context, [...command.split(" "), "--help"])).toBe(0);
  }
  expect(await run(h.context, ["--version"])).toBe(0); expect(h.accesses()).toBe(0);
});

test("CLI preserves .NET boolean values, token group help and end-of-options literal paths", async () => {
  const h = harness();
  for (const args of [["token", "--help"], ["token", "-h"], ["token", "-?"], ["show", "-?"], ["-?"]]) {
    expect(await run(h.context, args)).toBe(0);
  }
  expect(h.accesses()).toBe(0);
  for (const args of [["show", "--json=true"], ["show", "--json", "true"]]) {
    h.output.length = 0; expect(await run(h.context, args)).toBe(0); expect(JSON.parse(h.output[0]!).workspaceId).toBe(workspace);
  }
  for (const args of [["show", "--json=false"], ["show", "--json", "false"]]) {
    h.output.length = 0; expect(await run(h.context, args)).toBe(0); expect(h.output[0]).toContain("Server:");
  }
  const literal = join(h.context.cwd, "--help");
  h.registry.put({ path: literal, server, workspace: other }); h.registry.save();
  h.output.length = 0;
  expect(await run(h.context, ["show", "--json", "--", literal])).toBe(0);
  expect(JSON.parse(h.output[0]!).workspaceId).toBe(other);
  expect(await run(h.context, ["show", "--json=invalid"])).toBe(1);
});

test("GUID formats used by .NET registrations normalize to the same credential key", () => {
  for (const value of [workspace, workspace.toUpperCase(), workspace.replaceAll("-", ""), `{${workspace}}`, `(${workspace})`, ` ${workspace} `]) {
    expect(guid(value)).toBe(workspace);
    expect(credentialKey(server, value)).toBe(`devbuddy:${server}/${workspace}`);
  }
});

test("command argument failures have safe output and never read credentials or contact a server", async () => {
  const cases: string[][] = [
    ["register"], ["register", "--server", server], ["register", "--workspace", workspace],
    ["register", "--server"], ["register", "--workspace"],
    ["register", "--server", server, "--server", server, "--workspace", workspace],
    ["register", "--server", server, "--workspace", "invalid-secret-value"],
    ["list", "unexpected"], ["list", "--json"], ["list", "--server", server],
    ["show", "first", "second"], ["show", "--json=invalid-secret-value"],
    ["update", "--label"], ["update", "--project", "invalid-secret-value"],
    ["update", "--clear-project=invalid-secret-value"], ["unregister", "--force"],
    ["doctor", "--server", server], ["mcp-headers", "--dir"], ["mcp-headers", "--url"],
    ["mcp-headers", "unexpected"], ["mcp-bridge", "--dir"], ["mcp-bridge", "--unknown"],
    ["token"], ["token", "unknown"], ["token", "set"], ["token", "remove"],
    ["token", "set", "--server", server, "--workspace", workspace, "--force"],
    ["token", "remove", "--server", server, "--workspace", workspace, "--force=invalid-secret-value"],
  ];
  for (const args of cases) {
    const h = harness();
    let contacted = false;
    const before = readFileSync(h.registry.file, "utf8");
    const result = await run(h.context, args, {
      verify: async () => { contacted = true; throw new Error("must not contact"); },
      readToken: async () => { throw new Error("must not prompt"); },
    });
    expect(result).toBe(1);
    expect(h.accesses()).toBe(0); expect(contacted).toBe(false);
    expect(h.output).toEqual([]);
    expect(h.errors.join("\n")).not.toContain("invalid-secret-value");
    expect(readFileSync(h.registry.file, "utf8")).toBe(before);
  }
});

test("path matching keeps the .NET platform case rule and separator boundary", () => {
  const h = harness();
  const upper = join(h.context.home, "Customer");
  const lower = join(h.context.home, "customer");
  expect(contains(upper, join(lower, "child"))).toBe(process.platform !== "linux");
  expect(contains(upper, upper + "-old")).toBe(false);
  h.registry.put({ path: upper, server, workspace: other }); h.registry.save();
  expect(Registry.load(h.context.home).find(join(lower, "child"))?.workspace)
    .toBe(process.platform === "linux" ? undefined : other);
});

test("TTY handles hidden backspace, EOF, cancellation and restores raw mode", async () => {
  for (const [input, expected] of [["ab\x7fc\r", "ac"], ["\x04", null]] as const) {
    const stream = new PassThrough() as PassThrough & { isTTY: boolean; isRaw: boolean; setRawMode(mode: boolean): void };
    stream.isTTY = true; stream.isRaw = false; stream.setRawMode = mode => { stream.isRaw = mode; };
    const error = new PassThrough(); let output = "";
    error.on("data", value => {
      if (value.toString().includes("Machine token")) expect(stream.isRaw).toBe(true);
      output += value.toString();
    });
    const reading = readToken(stream, error); stream.write(input);
    expect(await reading).toBe(expected); expect(stream.isRaw).toBe(false); expect(output).toBe("Machine token (hidden): \n");
  }
  const stream = new PassThrough() as PassThrough & { isTTY: boolean; setRawMode(mode: boolean): void };
  stream.isTTY = true; stream.setRawMode = () => {};
  const reading = readToken(stream, new PassThrough()); stream.write("\x03");
  await expect(reading).rejects.toThrow("Cancelled");
});

test("longest checkout and separator boundaries match .NET without realpath", () => {
  const h = harness(), nested = join(h.context.cwd, "nested");
  h.registry.put({ path: nested, server: "HTTPS://EXAMPLE.INVALID:443/path", workspace: workspace.toUpperCase(), project: other }); h.registry.save();
  expect(Registry.load(h.context.home).find(join(nested, "child"))?.project).toBe(other);
  expect(contains(h.context.cwd, h.context.cwd + "-old")).toBe(false);
  expect(h.registry.find(h.context.cwd + "-old")).toBeUndefined();
  expect(readdirSync(h.context.home).filter(file => file.endsWith(".tmp"))).toEqual([]);
});

test("checkout finds .git directory and worktree file and refuses broad roots", () => {
  const h = harness(), child = join(h.context.cwd, "child"); mkdirSync(child);
  const git = join(h.context.cwd, ".git"); rmSync(git, { recursive: true }); writeFileSync(git, "gitdir: elsewhere");
  expect(checkoutRoot(child, h.context.userHome)).toBe(h.context.cwd);
  rmSync(git); mkdirSync(git);
  expect(checkoutRoot(child, h.context.userHome)).toBe(h.context.cwd);
  expect(() => checkoutRoot(h.context.home, h.context.userHome)).toThrow("too broad");
  expect(() => checkoutRoot(parse(h.context.home).root, h.context.userHome)).toThrow("too broad");
  expect(() => checkoutRoot(join(child, "missing"), h.context.userHome)).toThrow("does not exist");
});

test("file store uses .NET SHA256 key format, replaces atomically and deletes missing items", () => {
  const h = harness(), store = fileCredentialStore(h.context.home);
  const key = credentialKey("HTTPS://EXAMPLE.INVALID:443/anything", workspace.toUpperCase());
  expect(key).toBe(`devbuddy:${server}/${workspace}`);
  store.write(key, token);
  const file = join(h.context.home, "tokens", createHash("sha256").update(key).digest("hex"));
  expect(readFileSync(file, "utf8")).toBe(token);
  store.write(key, "b".repeat(43)); expect(store.read(key)).toBe("b".repeat(43));
  if (process.platform !== "win32") {
    expect(statSync(file).mode & 0o777).toBe(0o600);
    expect(statSync(dirname(file)).mode & 0o777).toBe(0o700);
  }
  expect(store.delete(key)).toBe(true); expect(store.delete(key)).toBe(false);
});

test("origin normalizes ports and IPv6, rejects userinfo and external HTTP", () => {
  expect(origin("http://[::1]:80/mcp")).toBe("http://[::1]");
  expect(origin("http://127.0.0.2:80/path")).toBe("http://127.0.0.2");
  expect(() => origin("https://user:password@example.invalid")).toThrow("credentials");
  expect(() => origin("http://example.invalid")).toThrow("HTTPS");
});

test("stdin consumes only first token line and handles EOF", async () => {
  expect(await readToken(Readable.from([token + "\nignored\n"])) ).toBe(token);
  expect(await readToken(Readable.from([]))).toBeNull();
  expect(await readToken(Readable.from([token]))).toBe(token);
});

test("verification negotiates protocol and refuses MCP errors even with HTTP 200", async () => {
  const calls: Rpc[] = [];
  const transport: Transport = async (_server, _token, message, protocol) => {
    calls.push(message);
    if (message.method === "initialize") return json({ jsonrpc: "2.0", id: 1, result: { protocolVersion: "2025-06-18" } });
    expect(protocol).toBe("2025-06-18"); expect(message.params.arguments.workspaceId).toBe(workspace);
    return json({ jsonrpc: "2.0", id: 2, result: { isError: true, content: [{ type: "text", text: token }] } });
  };
  await expect(verify(server, token, workspace, transport)).rejects.toThrow("not accepted");
  expect(calls.length).toBe(2);
  await expect(verify(server, token, workspace, async () => new Response(null, { status: 302 }))).rejects.toThrow("redirected");
});

test("verification refuses HTTP status failures without retaining remote response text", async () => {
  for (const status of [301, 302, 307, 308, 401, 403, 404, 405, 429, 500, 503]) {
    let calls = 0;
    const transport: Transport = async () => { calls++; return new Response(token, { status }); };
    let reason = "";
    try { await verify(server, token, workspace, transport); } catch (error) { reason = (error as Error).message; }
    expect(reason.length).toBeGreaterThan(0); expect(reason).not.toContain(token); expect(calls).toBe(1);
  }
});

test("malformed MCP envelope and tool results are rejected even with HTTP 200", async () => {
  for (const payload of [null, [], {}, { jsonrpc: "2.0" }, { jsonrpc: "2.0", id: 1 },
    { jsonrpc: "2.0", id: 1, result: {}, error: { code: -1, message: token } },
    { jsonrpc: "2.0", id: {}, result: {} }, { jsonrpc: "2.0", id: 1, error: token }]) {
    await expect(verify(server, token, workspace, async () => json(payload))).rejects.toThrow("malformed MCP");
  }
  for (const payload of ["not JSON", 'data: {"jsonrpc":"2.0"}\n\n']) {
    await expect(verify(server, token, workspace, async () => new Response(payload, { headers: { "content-type": "application/json" } }))).rejects.toThrow("malformed MCP");
  }
  for (const value of [null, {}, [], "accepted", { content: [], isError: "false" }]) {
    await expect(verify(server, token, workspace, async (_server, _token, message) => json({ jsonrpc: "2.0", id: message.id,
      result: message.method === "initialize" ? { protocolVersion: "2025-06-18" } : value }))).rejects.toThrow("valid MCP tool result");
  }
});

test("verification cancels open SSE response bodies after finding its matching result", async () => {
  let cancelled = 0;
  await verify(server, token, workspace, async (_server, _token, message) => {
    const result = message.method === "initialize" ? { protocolVersion: "2025-06-18" } : { content: [] };
    return new Response(new ReadableStream({ start(controller) {
      controller.enqueue(new TextEncoder().encode(`data: ${JSON.stringify({ jsonrpc: "2.0", id: message.id, result })}\n\n`));
    }, cancel() { cancelled++; } }), { headers: { "content-type": "text/event-stream" } });
  });
  expect(cancelled).toBe(2);
});

test("HTTP transport maps TLS, cancellation and network errors without exposing upstream text", async () => {
  const previous = globalThis.fetch;
  try {
    for (const [code, expected] of [["SELF_SIGNED_CERT_IN_CHAIN", "certificate"], ["CERT_HAS_EXPIRED", "certificate"], ["ECONNREFUSED", "could not be reached"]]) {
      globalThis.fetch = (async () => { throw new Error(token, { cause: { code } }); }) as unknown as typeof fetch;
      let reason = "";
      try { await post(server, token, {}, undefined, new AbortController().signal); } catch (error) { reason = (error as Error).message; }
      expect(reason).toContain(expected!); expect(reason).not.toContain(token);
    }
    const abort = new AbortController(); abort.abort();
    await expect(post(server, token, {}, undefined, abort.signal)).rejects.toThrow("cancelled or timed out");
  } finally { globalThis.fetch = previous; }
});

test("SSE handles multiline data, split CRLF, Thai UTF8, progress and empty 202", async () => {
  const bytes = new TextEncoder().encode(': comment\r\ndata: {"jsonrpc":"2.0",\r\ndata: "method":"notifications/progress","params":{"text":"ภาษาไทย"}}\r\n\r\ndata: {"jsonrpc":"2.0","id":1,"result":{}}\n\n');
  const stream = new ReadableStream<Uint8Array>({ start(controller) { for (const byte of bytes) controller.enqueue(new Uint8Array([byte])); controller.close(); } });
  const messages: Rpc[] = [];
  for await (const answer of answers(new Response(stream, { headers: { "content-type": "text/event-stream" } }))) messages.push(answer);
  expect(messages[0]?.params.text).toBe("ภาษาไทย"); expect(messages[1]?.id).toBe(1);
  const empty: Rpc[] = []; for await (const answer of answers(new Response(null, { status: 202 }))) empty.push(answer);
  expect(empty).toEqual([]);
});

async function bridgeRun(h: ReturnType<typeof harness>, messages: Rpc[], transport: Transport) {
  const output = new PassThrough(); let text = ""; output.on("data", value => { text += value.toString(); });
  await bridge(h.context, {}, Readable.from(messages.map(message => JSON.stringify(message) + "\n")), output, new AbortController().signal, transport);
  return text.trim().split("\n").filter(Boolean).map(line => JSON.parse(line) as Rpc);
}
test("bridge initializes before concurrent calls, injects workspace, preserves guidance and rereads token", async () => {
  const h = harness(); fileCredentialStore(h.context.home).write(credentialKey(server, workspace), token);
  let initialized = false, count = 0;
  const transport: Transport = async (_server, current, message, protocol) => {
    expect(current).toBe(token); count++;
    if (message.method === "initialize") {
      await new Promise(resolve => setTimeout(resolve, 10)); initialized = true;
      return json({ jsonrpc: "2.0", id: message.id, result: { protocolVersion: "2025-06-18", instructions: "Server guidance" } });
    }
    expect(initialized).toBe(true); expect(protocol).toBe("2025-06-18");
    expect(message.params.arguments.scope.workspaceId).toBe(workspace);
    expect(message.params.arguments.workspaceId).toBe(workspace);
    return json({ jsonrpc: "2.0", id: message.id, result: {} });
  };
  const output = await bridgeRun(h, [{ jsonrpc: "2.0", id: 1, method: "initialize" }, { jsonrpc: "2.0", id: 2, method: "tools/call", params: { name: "search", arguments: { workspaceId: other, scope: { workspaceId: other } } } }], transport);
  expect(output[0]?.result.instructions).toContain("Server guidance"); expect(count).toBe(2); expect(h.accesses()).toBe(2);
});

test("bridge holds tools until workspace choice and rejects changing origin/workspace", async () => {
  const h = harness();
  h.registry.put({ path: join(h.context.home, "elsewhere"), server: "https://other.invalid", workspace: other, label: "Other" }); h.registry.save();
  h.context.cwd = h.context.home;
  const store = fileCredentialStore(h.context.home); store.write(credentialKey(server, workspace), token); store.write(credentialKey("https://other.invalid", other), token);
  let requests = 0;
  const output = await bridgeRun(h, [
    { jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "search", arguments: {} } },
    { jsonrpc: "2.0", id: 2, method: "tools/call", params: { name: "use_workspace", arguments: { workspace } } },
    { jsonrpc: "2.0", id: 3, method: "tools/call", params: { name: "use_workspace", arguments: { workspace: other } } },
  ], async () => { requests++; return json({}); });
  expect(output.find(message => message.id === 1)?.result.isError).toBe(true);
  expect(output.filter(message => message.result.isError === false).length).toBe(1);
  expect(output.filter(message => message.result.isError === true).length).toBe(2);
  expect(requests).toBe(0);
  expect(bridgeTargets(h.context, { server, workspace, project: other })[0]?.project).toBe(other);
  expect(() => bridgeTargets(h.context, { server })).toThrow("together");
});

test("bridge malformed input never reaches a store or transport", async () => {
  const h = harness(); let requests = 0;
  const output = await bridgeRun(h, [{ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "search", arguments: "invalid" } }], async () => { requests++; return json({}); });
  expect(output[0]?.error.code).toBe(-32602); expect(requests).toBe(0); expect(h.accesses()).toBe(0);
});

test("bridge forwards post-initialize calls concurrently while preserving complete output lines", async () => {
  const h = harness(); fileCredentialStore(h.context.home).write(credentialKey(server, workspace), token);
  let release!: () => void;
  const slow = new Promise<void>(resolve => { release = resolve; });
  let concurrent = false;
  const transport: Transport = async (_server, _token, message) => {
    if (message.id === 1) await slow;
    else { concurrent = true; release(); }
    return json({ jsonrpc: "2.0", id: message.id, result: { text: "ภาษาไทย".repeat(100) } });
  };
  const chunks: string[] = []; let writes = 0, overlap = false;
  const output = new Writable({ highWaterMark: 1, write(chunk, _encoding, callback) {
    writes++; if (writes > 1) overlap = true;
    setTimeout(() => { chunks.push(chunk.toString()); writes--; callback(); }, 5);
  } });
  const input = Readable.from([1, 2].map(id => JSON.stringify({ jsonrpc: "2.0", id, method: "ping" }) + "\n"));
  await bridge(h.context, {}, input, output, new AbortController().signal, transport);
  expect(concurrent).toBe(true); expect(overlap).toBe(false);
  expect(chunks.length).toBe(2);
  expect(chunks.flatMap(chunk => chunk.trim().split("\n")).map(line => JSON.parse(line).result.text)).toEqual(["ภาษาไทย".repeat(100), "ภาษาไทย".repeat(100)]);
});

test("bridge follows token replacement/removal during one session and never retries refused HTTP calls", async () => {
  const h = harness(), input = new PassThrough(), output = new PassThrough();
  const store = fileCredentialStore(h.context.home), key = credentialKey(server, workspace); store.write(key, token);
  const tokens: string[] = []; let emitted = "";
  const waiters = new Map<number, () => void>();
  output.on("data", value => {
    emitted += value.toString();
    for (const line of value.toString().trim().split("\n")) { const message = JSON.parse(line); waiters.get(message.id)?.(); }
  });
  const work = bridge(h.context, {}, input, output, new AbortController().signal, async (_server, current, message) => {
    tokens.push(current); return message.id === 2 ? new Response(token, { status: 401 }) : json({ jsonrpc: "2.0", id: message.id, result: {} });
  });
  const call = (id: number) => new Promise<void>(resolve => { waiters.set(id, resolve); input.write(JSON.stringify({ jsonrpc: "2.0", id, method: "ping" }) + "\n"); });
  await call(1); store.write(key, "b".repeat(43)); await call(2); store.delete(key); await call(3); input.end(); await work;
  expect(tokens).toEqual([token, "b".repeat(43)]);
  expect(emitted).not.toContain(token); expect(emitted).not.toContain("b".repeat(43));
});

test("bridge log excludes arguments, token, response bodies and caller-supplied methods", async () => {
  const h = harness(), log = join(h.context.home, "bridge.log");
  fileCredentialStore(h.context.home).write(credentialKey(server, workspace), token);
  const input = Readable.from([JSON.stringify({ jsonrpc: "2.0", id: 1, method: token, params: { secret: token } }) + "\n"]);
  const output = new PassThrough(); output.resume();
  await bridge(h.context, { log }, input, output, new AbortController().signal, async () => new Response(token, { status: 429 }));
  const text = readFileSync(log, "utf8"); expect(text).not.toContain(token);
  const entry = JSON.parse(text.trim()); expect(entry.method).toBe("other"); expect(entry.status).toBe(429);
  expect(Object.keys(entry).sort()).toEqual(["method", "status", "timestamp"]);
});

test("bridge answers missing registrations locally so the host can explain setup", async () => {
  const h = harness(); h.registry.remove(h.context.cwd); h.registry.save();
  const output = await bridgeRun(h, [
    { jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2025-06-18" } },
    { jsonrpc: "2.0", id: 2, method: "tools/list" },
    { jsonrpc: "2.0", id: 3, method: "tools/call", params: { name: "search", arguments: {} } },
  ], async () => { throw new Error("must not call transport"); });
  expect(output[0]?.result.instructions).toContain("Nothing is registered");
  expect(output[1]?.result.tools[0].name).toBe("use_workspace");
  expect(output[2]?.result.isError).toBe(true); expect(h.accesses()).toBe(0);
});

test("bridge strips nested scope schema then fills missing scope workspace on the next call", async () => {
  const h = harness(), input = new PassThrough(), output = new PassThrough();
  fileCredentialStore(h.context.home).write(credentialKey(server, workspace), token);
  const replies = new Map<number, (message: Rpc) => void>();
  output.on("data", value => { for (const line of value.toString().trim().split("\n")) { const message = JSON.parse(line); replies.get(message.id)?.(message); } });
  let forwarded: Rpc | undefined;
  const work = bridge(h.context, {}, input, output, new AbortController().signal, async (_server, _token, message) => {
    if (message.method === "tools/list") return json({ jsonrpc: "2.0", id: message.id, result: { tools: [{ name: "search", inputSchema: {
      type: "object", properties: { scope: { type: "object", properties: { workspaceId: { type: "string" }, projectId: { type: "string" } }, required: ["workspaceId"] } },
    } }] } });
    forwarded = message; return json({ jsonrpc: "2.0", id: message.id, result: {} });
  });
  const call = (message: Rpc) => new Promise<Rpc>(resolve => { replies.set(message.id, resolve); input.write(JSON.stringify(message) + "\n"); });
  const list = await call({ jsonrpc: "2.0", id: 1, method: "tools/list" });
  expect(list.result.tools[0].inputSchema.properties.scope.properties.workspaceId).toBeUndefined();
  expect(list.result.tools[0].inputSchema.properties.scope.required).toEqual([]);
  await call({ jsonrpc: "2.0", id: 2, method: "tools/call", params: { name: "search", arguments: {} } }); input.end(); await work;
  expect(forwarded?.params.arguments.scope.workspaceId).toBe(workspace);
});

test("bridge cancellation closes stdin and cancels an in-flight transport", async () => {
  const h = harness(), input = new PassThrough(), output = new PassThrough(); output.resume();
  fileCredentialStore(h.context.home).write(credentialKey(server, workspace), token);
  const cancellation = new AbortController(); let entered!: () => void;
  const started = new Promise<void>(resolve => { entered = resolve; });
  const work = bridge(h.context, {}, input, output, cancellation.signal, async (_server, _token, _message, _protocol, signal) => {
    entered(); await new Promise<void>(resolve => signal.addEventListener("abort", () => resolve(), { once: true }));
    throw new Error("cancelled");
  });
  input.write(JSON.stringify({ jsonrpc: "2.0", id: 1, method: "ping" }) + "\n");
  await started; cancellation.abort(); await work; expect(cancellation.signal.aborted).toBe(true); input.destroy();
});
