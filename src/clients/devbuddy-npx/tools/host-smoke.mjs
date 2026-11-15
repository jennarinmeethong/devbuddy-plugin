// Local host-connection drill with isolated app settings, synthetic MCP data and no model turn.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { createInterface } from 'node:readline';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Registry } from '../dist/registry.js';
import { credentialKey, fileCredentialStore } from '../dist/file-store.js';

const host = process.argv[2];
assert(['codex', 'claude'].includes(host), 'Choose codex or claude');
const root = fileURLToPath(new URL('../', import.meta.url));
mkdirSync(join(root, 'out'), { recursive: true });
const evidence = mkdtempSync(join(root, 'out/host-' + host + '-'));
const home = join(evidence, 'client-home'), appHome = join(evidence, 'app-home'), cwd = join(evidence, 'งาน host checkout');
mkdirSync(appHome); mkdirSync(cwd);
const token = 'h'.repeat(43), workspace = '01234567-89ab-cdef-0123-456789abcdef';
let authenticated = 0;
const methods = new Set();
const server = createServer(async (request, response) => {
  if (request.method !== 'POST' || request.url !== '/mcp') { response.writeHead(404).end(); return; }
  if (request.headers.authorization !== `Bearer ${token}`) { response.writeHead(401).end(); return; }
  authenticated++;
  let text = ''; for await (const chunk of request) text += chunk;
  const message = JSON.parse(text); methods.add(message.method);
  if (!Object.hasOwn(message, 'id')) { response.writeHead(202).end(); return; }
  const result = message.method === 'initialize' ? { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'synthetic-devbuddy', version: '1' } }
    : message.method === 'tools/list' ? { tools: [{ name: 'list_projects', description: 'Synthetic host acceptance fixture', inputSchema: { type: 'object', properties: { workspaceId: { type: 'string' } }, required: ['workspaceId'] } }] }
    : { content: [{ type: 'text', text: '{"projects":[]}' }] };
  response.setHeader('content-type', 'application/json'); response.end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const registry = Registry.load(home); registry.put({ path: cwd, server: origin, workspace }); registry.save();
fileCredentialStore(home).write(credentialKey(origin, workspace), token);
function quote(value) {
  if (process.platform === 'win32') {
    assert(!/["%!$`\r\n]/.test(value), 'Unexpected metacharacter in a test argument'); return `"${value}"`;
  }
  return "'" + value.replaceAll("'", "'\\''") + "'";
}
const entry = resolve(process.env.DEVBUDDY_TEST_ENTRY ?? join(root, 'dist/main.js'));
const traceEntry = join(evidence, 'isolated-helper.mjs');
// Codex deliberately removes the custom test-home environment from its helper.
// Set only this synthetic registry/store inside the test wrapper, never a token.
writeFileSync(traceEntry, `process.env.DEVBUDDY_HOME=${JSON.stringify(home)};\nprocess.env.DEVBUDDY_CREDENTIAL_STORE='file';\nawait import(${JSON.stringify(pathToFileURL(entry).href)});\n`);
const helper = [process.execPath, traceEntry, 'mcp-headers', '--dir', cwd, '--url', origin + '/mcp'].map(quote).join(' ');
const env = { ...process.env, CODEX_HOME: appHome, CLAUDE_CONFIG_DIR: appHome, DEVBUDDY_HOME: home, DEVBUDDY_CREDENTIAL_STORE: 'file', DISABLE_AUTOUPDATER: '1' };
for (const name of ['OPENAI_API_KEY', 'ANTHROPIC_API_KEY', 'CLAUDE_CODE_OAUTH_TOKEN']) delete env[name];
const children = [];
function launch(command, args) {
  const child = spawn(command, args, { cwd, env, shell: false, stdio: ['pipe', 'pipe', 'pipe'] }); children.push(child); return child;
}
async function command(executable, args) {
  const child = launch(executable, args);
  return new Promise((resolve, reject) => {
    let stdout = '', stderr = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error('Host command timed out')); }, 45_000);
    child.stdout.on('data', value => { stdout += value; }); child.stderr.on('data', value => { stderr += value; });
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('close', code => { clearTimeout(timer); resolve({ code, stdout, stderr }); }); child.stdin.end();
  });
}
try {
  if (host === 'claude') {
    const executable = process.env.DEVBUDDY_TEST_CLAUDE_EXE ?? 'claude';
    const added = await command(executable, ['mcp', 'add-json', '--scope', 'user', 'devbuddy_npx_test', JSON.stringify({ type: 'http', url: origin + '/mcp', headersHelper: helper })]);
    assert.equal(added.code, 0, 'Claude could not add the isolated MCP configuration');
    const checked = await command(executable, ['mcp', 'get', 'devbuddy_npx_test']);
    assert.equal(checked.code, 0, 'Claude MCP health check failed');
    assert(authenticated > 0 && methods.has('initialize'), 'Claude did not connect with helper credentials');
  } else {
    const codex = process.env.DEVBUDDY_TEST_CODEX_JS;
    assert(codex, 'Set DEVBUDDY_TEST_CODEX_JS to the installed Codex JavaScript launcher');
    writeFileSync(join(appHome, 'config.toml'), `[mcp_servers.devbuddy_npx_test]\nurl = ${JSON.stringify(origin + '/mcp')}\nhttp_headers_helper = ${JSON.stringify(helper)}\nstartup_timeout_sec = 20\n`);
    const child = launch(process.execPath, [codex, 'app-server', '--stdio']); child.stderr.resume();
    const lines = createInterface({ input: child.stdout });
    let id = 0;
    const pending = new Map();
    lines.on('line', line => {
      let message; try { message = JSON.parse(line); } catch { return; }
      const waiting = pending.get(message.id); if (waiting) { clearTimeout(waiting.timer); pending.delete(message.id); waiting.resolve(message); }
    });
    async function rpc(method, params) {
      const requestId = ++id;
      const reply = new Promise((resolve, reject) => {
        const timer = setTimeout(() => { pending.delete(requestId); reject(new Error('Codex host RPC timed out')); }, 40_000);
        pending.set(requestId, { resolve, timer });
      });
      child.stdin.write(JSON.stringify({ id: requestId, method, params }) + '\n'); return reply;
    }
    const initialized = await rpc('initialize', { clientInfo: { name: 'devbuddy-host-acceptance', version: '1' }, capabilities: { experimentalApi: true } });
    assert(!initialized.error, 'Codex app-server initialization failed');
    child.stdin.write(JSON.stringify({ method: 'initialized' }) + '\n');
    const status = await rpc('mcpServerStatus/list', { serverName: 'devbuddy_npx_test', detail: 'toolsAndAuthOnly', limit: 100 });
    assert(!status.error, 'Codex MCP discovery failed');
    writeFileSync(join(evidence, 'codex-discovery.json'), JSON.stringify(status, null, 2));
    assert(authenticated > 0 && methods.has('initialize') && methods.has('tools/list'), 'Codex did not discover tools through the helper');
    child.stdin.end(); lines.close();
  }
  const report = { passed: true, host, node: process.version, platform: process.platform, architecture: process.arch,
    authenticatedRequests: authenticated, methods: [...methods], isolatedAppConfig: true, syntheticRegistryInjectedByTestWrapper: true, modelTurnStarted: false,
    fixture: 'Synthetic MCP connection only; real DevBuddy stack is tested separately.', evidence };
  writeFileSync(join(evidence, 'result.json'), JSON.stringify(report, null, 2)); process.stdout.write(JSON.stringify(report, null, 2) + '\n');
} finally {
  for (const child of children) if (child.exitCode === null) child.kill();
  server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
}
