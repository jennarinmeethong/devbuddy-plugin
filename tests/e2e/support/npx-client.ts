import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { createServer, type Server } from "node:http";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { createInterface } from "node:readline";
import { Readable } from "node:stream";
import type { ReadableStream as NodeReadableStream } from "node:stream/web";
import { env } from "./env";

export interface Reply {
  id?: number;
  result?: {
    protocolVersion?: string;
    instructions?: string;
    tools?: { name: string; inputSchema: unknown }[];
    content?: { type: string; text?: string }[];
    structuredContent?: unknown;
    isError?: boolean;
  };
  error?: { code: number; message: string };
}

/** Real production executable against a transparent loopback gateway to the real MCP service.
 * Plain HTTP between Compose services is test topology; the client still refuses remote HTTP.
 * The gateway synthesizes no MCP responses and does not record credentials or payloads.
 */
export class NpxClient {
  readonly root = mkdtempSync(path.join(tmpdir(), "devbuddy-npx-e2e-"));
  readonly home = path.join(this.root, "registry");
  private readonly entry = process.env.DEVBUDDY_E2E_NPX_ENTRY ?? "/repo/npm-client/dist/main.js";
  private gateway?: Server;
  origin = "";

  async start(): Promise<void> {
    this.gateway = createServer(async (request, response) => {
      try {
        const body: Buffer[] = [];
        for await (const chunk of request) body.push(Buffer.from(chunk));
        const upstream = await fetch(`${env.mcpURL}${request.url}`, {
          method: request.method, redirect: "manual", body: Buffer.concat(body),
          headers: {
            "content-type": "application/json", accept: "application/json, text/event-stream",
            ...(request.headers.authorization ? { authorization: request.headers.authorization } : {}),
            ...(request.headers["mcp-protocol-version"] ? { "mcp-protocol-version": String(request.headers["mcp-protocol-version"]) } : {}),
          },
        });
        response.writeHead(upstream.status, { "content-type": upstream.headers.get("content-type") ?? "application/json" });
        // DOM and Node declarations describe the same runtime WHATWG stream differently.
        if (upstream.body) Readable.fromWeb(upstream.body as unknown as NodeReadableStream<Uint8Array>).pipe(response); else response.end();
      } catch { response.writeHead(502).end(); }
    });
    await new Promise<void>(resolve => this.gateway!.listen(0, "127.0.0.1", resolve));
    const address = this.gateway.address();
    if (!address || typeof address === "string") throw new Error("Loopback test gateway did not start.");
    this.origin = `http://127.0.0.1:${address.port}`;
  }

  checkout(name: string): string {
    const directory = path.join(this.root, name);
    mkdirSync(path.join(directory, ".git"), { recursive: true });
    return directory;
  }

  private startProcess(args: string[], cwd: string): ChildProcessWithoutNullStreams {
    return spawn(process.execPath, [this.entry, ...args], { cwd, shell: false,
      env: { ...process.env, DEVBUDDY_HOME: this.home, DEVBUDDY_CREDENTIAL_STORE: "file" }, stdio: ["pipe", "pipe", "pipe"] });
  }

  async command(args: string[], input = "", cwd = this.root): Promise<{ code: number | null; stdout: string; stderr: string }> {
    const child = this.startProcess(args, cwd);
    return new Promise((resolve, reject) => {
      let stdout = "", stderr = "";
      const timeout = setTimeout(() => { child.kill(); reject(new Error("npm CLI timed out.")); }, 30_000);
      child.stdout.on("data", chunk => { stdout += chunk; }); child.stderr.on("data", chunk => { stderr += chunk; });
      child.once("error", error => { clearTimeout(timeout); reject(error); });
      child.once("close", code => { clearTimeout(timeout); resolve({ code, stdout, stderr }); });
      child.stdin.end(input);
    });
  }

  bridge(cwd = this.root): BridgeSession { return new BridgeSession(this.startProcess(["mcp-bridge"], cwd)); }

  async dispose(): Promise<void> {
    if (this.gateway) await new Promise<void>(resolve => { this.gateway!.closeAllConnections(); this.gateway!.close(() => resolve()); });
    rmSync(this.root, { recursive: true, force: true });
  }
}

export class BridgeSession {
  private id = 0;
  private readonly pending = new Map<number, { resolve(reply: Reply): void; reject(error: Error): void; timer: ReturnType<typeof setTimeout> }>();
  private readonly exited: Promise<void>;

  constructor(private readonly process: ChildProcessWithoutNullStreams) {
    process.stderr.resume();
    const lines = createInterface({ input: process.stdout });
    lines.on("line", line => {
      let reply: Reply;
      try { reply = JSON.parse(line) as Reply; } catch { return; }
      if (reply.id === undefined) return;
      const waiting = this.pending.get(reply.id);
      if (waiting) { clearTimeout(waiting.timer); this.pending.delete(reply.id); waiting.resolve(reply); }
    });
    this.exited = new Promise(resolve => process.once("close", () => {
      lines.close();
      for (const waiting of this.pending.values()) { clearTimeout(waiting.timer); waiting.reject(new Error("npm bridge exited before replying.")); }
      this.pending.clear(); resolve();
    }));
  }

  async rpc(method: string, params: Record<string, unknown> = {}): Promise<Reply> {
    const id = ++this.id;
    const response = new Promise<Reply>((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error("npm bridge response timed out.")); }, 25_000);
      this.pending.set(id, { resolve, reject, timer });
    });
    this.process.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n");
    return response;
  }

  async initialize(): Promise<Reply> {
    const reply = await this.rpc("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "npm-e2e", version: "1" } });
    this.process.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" }) + "\n");
    return reply;
  }

  call(name: string, args: Record<string, unknown>): Promise<Reply> { return this.rpc("tools/call", { name, arguments: args }); }

  async close(): Promise<void> {
    this.process.stdin.end();
    const timeout = setTimeout(() => this.process.kill(), 10_000);
    try { await this.exited; } finally { clearTimeout(timeout); }
  }
}
