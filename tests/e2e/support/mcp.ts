import { request, type APIRequestContext, type APIResponse } from "@playwright/test";
import { env } from "./env";

/**
 * A minimal MCP client over the streamable HTTP transport, which is what a self-hosted deployment
 * exposes. It speaks just enough JSON-RPC to list tools and call them, and nothing about it is
 * DevBuddy-specific: that is the point, since an assistant knows nothing about this server either.
 *
 * The bearer is the API's own access token. The HTTP transport shares the API identity, and every
 * call it makes runs on the AI channel, whatever the token was issued for.
 */

export interface ToolResult {
  isError: boolean;
  text: string;
  structured: unknown;
}

interface RpcResponse {
  jsonrpc: "2.0";
  id?: number;
  result?: unknown;
  error?: { code: number; message: string };
}

export class McpSession {
  private nextId = 1;
  private sessionId: string | null = null;

  private constructor(private readonly http: APIRequestContext) {}

  static async open(accessToken: string): Promise<McpSession> {
    const http = await request.newContext({
      baseURL: env.mcpURL,
      extraHTTPHeaders: { authorization: `Bearer ${accessToken}` },
    });

    const session = new McpSession(http);

    await session.rpc("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "devbuddy-e2e", version: "1.0.0" },
    });

    await session.send({ jsonrpc: "2.0", method: "notifications/initialized" });

    return session;
  }

  /** The unauthenticated request, for the test that proves there is no such thing. */
  static async post(accessToken: string | null, body: unknown): Promise<APIResponse> {
    const http = await request.newContext({ baseURL: env.mcpURL });

    try {
      const response = await http.post("/", {
        data: body,
        headers: {
          accept: "application/json, text/event-stream",
          ...(accessToken ? { authorization: `Bearer ${accessToken}` } : {}),
        },
      });

      await response.body();
      return response;
    } finally {
      await http.dispose();
    }
  }

  async listTools(): Promise<{ name: string; description?: string }[]> {
    const result = (await this.rpc("tools/list", {})) as { tools: { name: string; description?: string }[] };
    return result.tools;
  }

  async callTool(name: string, args: Record<string, unknown>): Promise<ToolResult> {
    const result = (await this.rpc("tools/call", { name, arguments: args })) as {
      isError?: boolean;
      content?: { type: string; text?: string }[];
      structuredContent?: unknown;
    };

    return {
      isError: result.isError ?? false,
      text: (result.content ?? []).map((part) => part.text ?? "").join("\n"),
      structured: result.structuredContent,
    };
  }

  /** Calls a tool that is expected to succeed, and returns what it answered. */
  async expectTool<T>(name: string, args: Record<string, unknown>): Promise<T> {
    const result = await this.callTool(name, args);

    if (result.isError) {
      throw new Error(`${name} was refused over MCP: ${result.text}`);
    }

    return (result.structured ?? JSON.parse(result.text)) as T;
  }

  async close(): Promise<void> {
    if (this.sessionId) {
      await this.http.delete("/", { headers: { "mcp-session-id": this.sessionId } }).catch(() => undefined);
    }

    await this.http.dispose();
  }

  private async rpc(method: string, params: unknown): Promise<unknown> {
    const id = this.nextId++;
    const response = await this.send({ jsonrpc: "2.0", id, method, params });
    const message = (await readMessages(response)).find((candidate) => candidate.id === id);

    if (!message) {
      throw new Error(`No JSON-RPC answer to ${method} (HTTP ${response.status()}).`);
    }

    if (message.error) {
      throw new Error(`${method} failed: ${message.error.code} ${message.error.message}`);
    }

    return message.result;
  }

  private async send(body: unknown): Promise<APIResponse> {
    const response = await this.http.post("/", {
      data: body,
      headers: {
        accept: "application/json, text/event-stream",
        ...(this.sessionId ? { "mcp-session-id": this.sessionId } : {}),
      },
    });

    if (response.status() >= 400) {
      throw new Error(`MCP answered HTTP ${response.status()}: ${await response.text()}`);
    }

    this.sessionId ??= response.headers()["mcp-session-id"] ?? null;
    return response;
  }
}

/** A JSON body, or a server-sent event stream carrying JSON-RPC messages in its data lines. */
async function readMessages(response: APIResponse): Promise<RpcResponse[]> {
  const text = await response.text();

  if ((response.headers()["content-type"] ?? "").includes("text/event-stream")) {
    return text
      .split(/\r?\n\r?\n/)
      .map((event) =>
        event
          .split(/\r?\n/)
          .filter((line) => line.startsWith("data:"))
          .map((line) => line.slice(5).trimStart())
          .join("\n"),
      )
      .filter((data) => data !== "")
      .map((data) => JSON.parse(data) as RpcResponse);
  }

  const parsed = JSON.parse(text) as RpcResponse | RpcResponse[];
  return Array.isArray(parsed) ? parsed : [parsed];
}
