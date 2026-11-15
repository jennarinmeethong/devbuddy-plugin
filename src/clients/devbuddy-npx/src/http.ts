import { origin } from "./registry.js";
import { ClientError } from "./errors.js";

export type Rpc = Record<string, any>;
export type Transport = (server: string, token: string, message: Rpc, protocol: string | undefined, signal: AbortSignal) => Promise<Response>;

export const post: Transport = async (server, token, message, protocol, signal) => {
  try { return await fetch(`${origin(server)}/mcp`, {
  method: "POST", redirect: "manual", signal,
  headers: {
    "Content-Type": "application/json", Accept: "application/json, text/event-stream",
    Authorization: `Bearer ${token}`, ...(protocol ? { "MCP-Protocol-Version": protocol } : {}),
  },
  body: JSON.stringify(message),
  }); } catch (error) {
    if (signal.aborted) throw new ClientError("The request was cancelled or timed out.");
    const code = (error as { cause?: { code?: string } })?.cause?.code;
    if (code && /CERT|TLS|SELF_SIGNED|UNABLE_TO_VERIFY|UNABLE_TO_GET_ISSUER/.test(code)) {
      throw new ClientError("The server certificate is not trusted. Configure the organization's root CA with NODE_EXTRA_CA_CERTS and restart the host.");
    }
    throw new ClientError("The server could not be reached. Check its address and network connection.");
  }
};

export function statusProblem(status: number): string {
  if (status === 401 || status === 403) return "The server refused the token or workspace. Check Plugin access.";
  if (status === 404 || status === 405) return "No MCP endpoint was found at /mcp.";
  if (status >= 300 && status < 400) return "The server redirected the request. Correct the registered server address.";
  if (status === 429) return "The server rate limit was reached. Try again later.";
  return `The server returned HTTP ${status}.`;
}

export async function* answers(response: Response): AsyncGenerator<Rpc> {
  if (response.status === 202 || response.status === 204) return;
  if (!response.headers.get("content-type")?.includes("text/event-stream")) {
    const text = await response.text();
    if (!text.trim()) return;
    yield parseAnswer(text);
    return;
  }
  if (!response.body) return;
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let pending = "";
  let lines: string[] = [];
  function event(line: string): Rpc | undefined {
    if (line === "") {
      const data = lines.join("\n"); lines = [];
      return data ? parseAnswer(data) : undefined;
    }
    if (line.startsWith("data:")) lines.push(line.slice(5).replace(/^ /, ""));
    return undefined;
  }
  try {
    while (true) {
      const { done, value } = await reader.read();
      pending += decoder.decode(value, { stream: !done });
      // Retain a trailing CR until the next chunk so split CRLF is one newline.
      let match: RegExpMatchArray | null;
      while ((match = pending.match(/\r\n|\n|\r(?!$)/)) !== null) {
        const line = pending.slice(0, match.index!);
        pending = pending.slice(match.index! + match[0].length);
        const message = event(line);
        if (message) yield message;
      }
      if (done) break;
    }
    if (pending) { const message = event(pending.replace(/\r$/, "")); if (message) yield message; }
    const last = event(""); if (last) yield last;
  } finally {
    // Verification stops at its matching result. Cancel a still-open SSE body so
    // that an early return cannot retain a connection until the timeout expires.
    try { await reader.cancel(); } catch { /* The stream may already have failed. */ }
    reader.releaseLock();
  }
}

function parseAnswer(text: string): Rpc {
  try {
    const value = JSON.parse(text);
    if (!value || typeof value !== "object" || Array.isArray(value) || value.jsonrpc !== "2.0") throw new Error();
    if (Object.hasOwn(value, "id")) {
      if (value.id !== null && typeof value.id !== "string" && !(typeof value.id === "number" && Number.isFinite(value.id))) throw new Error();
      if (Object.hasOwn(value, "result") === Object.hasOwn(value, "error")) throw new Error();
      if (Object.hasOwn(value, "error") && (!value.error || typeof value.error !== "object" || !Number.isInteger(value.error.code) || typeof value.error.message !== "string")) throw new Error();
    } else if (typeof value.method !== "string" || (value.params !== undefined && (!value.params || typeof value.params !== "object" || Array.isArray(value.params)))) throw new Error();
    return value;
  } catch { throw new ClientError("The server returned a malformed MCP response."); }
}

export async function verify(server: string, token: string, workspace: string, transport: Transport = post, signal?: AbortSignal): Promise<void> {
  const timeout = AbortSignal.timeout(20_000);
  const cancellation = signal ? AbortSignal.any([signal, timeout]) : timeout;
  async function request(message: Rpc, protocol?: string): Promise<Rpc> {
    const response = await transport(server, token, message, protocol, cancellation);
    if (!response.ok) throw new ClientError(statusProblem(response.status));
    for await (const answer of answers(response)) if (answer.id === message.id) return answer;
    throw new ClientError("The server did not return an MCP result.");
  }
  const initialized = await request({ jsonrpc: "2.0", id: 1, method: "initialize", params: {
    protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "devbuddy-npx", version: "1" },
  } });
  if (initialized.error || typeof initialized.result?.protocolVersion !== "string") throw new ClientError("MCP initialization failed.");
  const result = await request({ jsonrpc: "2.0", id: 2, method: "tools/call", params: {
    name: "list_projects", arguments: { workspaceId: workspace },
  } }, initialized.result.protocolVersion);
  if (result.error || result.result?.isError === true) throw new ClientError("The token was not accepted in this workspace. Check Plugin access.");
  if (!result.result || typeof result.result !== "object" || !Array.isArray(result.result.content)
    || (result.result.isError !== undefined && typeof result.result.isError !== "boolean")) {
    throw new ClientError("The server did not return a valid MCP tool result.");
  }
}
