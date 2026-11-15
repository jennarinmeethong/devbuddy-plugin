// Actual installed-artifact/profile integration. Uses only synthetic loopback data.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { randomUUID } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { createInterface } from 'node:readline';
import { prepare, profiles, artifactHash } from './prepare.mjs';

const repository = resolve(import.meta.dirname, '../..');
const out = join(repository, 'out'); mkdirSync(out, { recursive: true });
const evidence = mkdtempSync(join(out, 'pilot-default-'));
const checkout = join(evidence, 'งาน checkout'); mkdirSync(checkout);
const home = join(evidence, 'synthetic-home');
const target = join(evidence, 'ติดตั้ง pilot');
const archive = resolve(process.argv[2] ?? join(repository, 'src/clients/devbuddy-npx/out/package-smoke-WSk8pm/devbuddy-plugin-client-1.14.0-next.0.tgz'));
const templates = resolve(process.argv[3] ?? join(repository, 'plugins'));
const installed = prepare({ archive, checkout, home, output: target, templates });
assert.equal(installed.artifactSha256, artifactHash);
assert.throws(() => prepare({ archive, checkout, home, output: target, templates: join(repository, 'plugins') }), /already exists/);
assert.throws(() => profiles({ node: process.execPath, entry: installed.installedEntry, checkout: checkout + '\n', home }));
const { Registry } = await import(pathToFileURL(join(target, 'client/node_modules/devbuddy-plugin-client/dist/registry.js')));
const { fileCredentialStore, credentialKey } = await import(pathToFileURL(join(target, 'client/node_modules/devbuddy-plugin-client/dist/file-store.js')));
const token = 'p'.repeat(43), workspace = randomUUID();
let calls = 0;
const methods = new Set();
const server = createServer(async (request, response) => {
  assert.equal(request.headers.authorization, 'Bearer ' + token);
  let text = ''; for await (const chunk of request) text += chunk;
  const message = JSON.parse(text);
  methods.add(message.method);
  if (!Object.hasOwn(message, 'id')) { response.writeHead(202).end(); return; }
  let result;
  if (message.method === 'initialize') result = { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'pilot-fixture', version: '1' } };
  else if (message.method === 'tools/list') result = { tools: [{ name: 'list_projects', inputSchema: { type: 'object', properties: { workspaceId: { type: 'string' } }, required: ['workspaceId'] } }] };
  else { assert.equal(message.params.arguments.workspaceId, workspace); calls++; result = { content: [{ type: 'text', text: '[]' }] }; }
  response.setHeader('content-type', 'application/json'); response.end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const registry = Registry.load(home); registry.put({ path: checkout, server: origin, workspace }); registry.save();
fileCredentialStore(home).write(credentialKey(origin, workspace), token);
const input = [
  { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18' } },
  { jsonrpc: '2.0', id: 2, method: 'tools/list' },
  { jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'list_projects', arguments: { workspaceId: randomUUID() } } },
].map(value => JSON.stringify(value)).join('\n') + '\n';
const checks = [];
const children = [];
try {
  const codex = readFileSync(join(target, 'codex/config.toml'), 'utf8');
  const configurations = [
    ['claude', JSON.parse(readFileSync(join(target, 'claude-plugin/.mcp.json'), 'utf8')).mcpServers.devbuddy],
    ['cowork', JSON.parse(readFileSync(join(target, 'cowork-plugin/.mcp.json'), 'utf8')).mcpServers.devbuddy],
    ['desktop', JSON.parse(readFileSync(join(target, 'claude-desktop.json'), 'utf8')).mcpServers.devbuddy],
    ['codex', { command: JSON.parse(codex.match(/^command = (.+)$/m)[1]), args: JSON.parse(codex.match(/^args = (.+)$/m)[1]), env: { DEVBUDDY_HOME: home } }],
  ];
  for (const [host, config] of configurations) {
    assert.equal(config.command, process.execPath);
    assert(config.args.includes(installed.installedEntry));
    assert(!config.args.includes('npx') && !config.args.includes('devbuddy'));
    const child = spawn(config.command, config.args, { cwd: evidence, shell: false,
      env: { ...process.env, PATH: '', ...config.env, DEVBUDDY_CREDENTIAL_STORE: 'file' }, stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    child.stdout.on('data', chunk => { stdout += chunk; }); child.stderr.on('data', chunk => { stderr += chunk; });
    const code = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { child.kill(); reject(new Error('Profile bridge timed out')); }, 30_000);
      child.once('error', error => { clearTimeout(timer); reject(error); });
      child.once('close', code => { clearTimeout(timer); resolve(code); }); child.stdin.end(input);
    });
    assert.equal(code, 0, stderr); assert(!(stdout + stderr).includes(token));
    const messages = stdout.trim().split('\n').map(line => JSON.parse(line));
    assert(messages.find(message => message.id === 1)?.result);
    assert(messages.find(message => message.id === 2)?.result.tools);
    assert(messages.find(message => message.id === 3)?.result);
    checks.push({ host, passed: true, emptyPath: true, wrongWorkingDirectory: true, initialized: true, toolCall: true });
  }
  for (const host of ['claude', 'cowork']) {
    const relative = 'skills/devbuddy/SKILL.md';
    assert.equal(readFileSync(join(target, host + '-plugin', relative), 'utf8'), readFileSync(join(templates, host, relative), 'utf8'));
  }
  assert.equal(readFileSync(join(target, 'codex/AGENTS.md'), 'utf8'), readFileSync(join(templates, 'codex/AGENTS.md'), 'utf8'));
  assert.equal(calls, configurations.length);
  const realHosts = [];
  const fixtureConfig = { ...configurations[0][1], env: { ...configurations[0][1].env, DEVBUDDY_CREDENTIAL_STORE: 'file' } };
  function launch(command, args, appHome) {
    const child = spawn(command, args, { cwd: evidence, shell: false, stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, CODEX_HOME: appHome, CLAUDE_CONFIG_DIR: appHome, DISABLE_AUTOUPDATER: '1' } });
    children.push(child); return child;
  }
  async function cli(command, args, appHome) {
    const child = launch(command, args, appHome); let output = '';
    child.stdout.on('data', chunk => { output += chunk; }); child.stderr.resume();
    const code = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { child.kill(); reject(new Error('Host health check timed out')); }, 60_000);
      child.once('error', error => { clearTimeout(timer); reject(error); });
      child.once('close', code => { clearTimeout(timer); resolve(code); }); child.stdin.end();
    });
    assert.equal(code, 0, 'Host configuration/health command failed'); assert(!output.includes(token));
  }
  if (process.env.DEVBUDDY_TEST_CLAUDE_EXE) {
    const appHome = join(evidence, 'claude-app-home'); mkdirSync(appHome); methods.clear();
    await cli(process.env.DEVBUDDY_TEST_CLAUDE_EXE, ['mcp', 'add-json', '--scope', 'user', 'devbuddy', JSON.stringify(fixtureConfig)], appHome);
    await cli(process.env.DEVBUDDY_TEST_CLAUDE_EXE, ['mcp', 'get', 'devbuddy'], appHome);
    assert(methods.has('initialize'), 'Claude did not initialize the generated stdio profile');
    realHosts.push({ host: 'claude-code-cli', passed: true, modelTurnStarted: false });
  }
  if (process.env.DEVBUDDY_TEST_CODEX_JS || process.env.DEVBUDDY_TEST_CODEX_EXE) {
    const appHome = join(evidence, 'codex-app-home'); mkdirSync(appHome); methods.clear();
    writeFileSync(join(appHome, 'config.toml'), codex + 'DEVBUDDY_CREDENTIAL_STORE = "file"\n');
    const child = process.env.DEVBUDDY_TEST_CODEX_EXE
      ? launch(process.env.DEVBUDDY_TEST_CODEX_EXE, ['app-server', '--stdio'], appHome)
      : launch(process.execPath, [process.env.DEVBUDDY_TEST_CODEX_JS, 'app-server', '--stdio'], appHome);
    child.stderr.resume();
    const lines = createInterface({ input: child.stdout }), pending = new Map(); let sequence = 0;
    lines.on('line', line => {
      let message; try { message = JSON.parse(line); } catch { return; }
      const waiting = pending.get(message.id);
      if (waiting) { clearTimeout(waiting.timer); pending.delete(message.id); waiting.resolve(message); }
    });
    async function rpc(method, params) {
      const id = ++sequence;
      const reply = new Promise((resolve, reject) => {
        const timer = setTimeout(() => { pending.delete(id); reject(new Error('Codex RPC deadline')); }, 60_000);
        pending.set(id, { resolve, timer });
      });
      child.stdin.write(JSON.stringify({ id, method, params }) + '\n'); return reply;
    }
    const initialized = await rpc('initialize', { clientInfo: { name: 'internal-pilot-test', version: '1' }, capabilities: { experimentalApi: true } });
    assert(!initialized.error); child.stdin.write(JSON.stringify({ method: 'initialized' }) + '\n');
    const status = await rpc('mcpServerStatus/list', { serverName: 'devbuddy', detail: 'toolsAndAuthOnly', limit: 100 });
    assert(!status.error); assert(methods.has('initialize') && methods.has('tools/list'));
    child.stdin.end(); lines.close(); realHosts.push({ host: 'codex-app-server', passed: true, modelTurnStarted: false });
  }
  const report = { passed: true, artifactSha256: artifactHash, node: process.version, platform: process.platform,
    architecture: process.arch, syntheticOnly: true, realHostAppsTested: realHosts, fixtureOnlyStoreOverride: true, defaultPluginsUnchanged: true,
    instructionFilesPreserved: true, checks, generated: target, evidence };
  writeFileSync(join(evidence, 'result.json'), JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} finally { for (const child of children) if (child.exitCode === null && child.signalCode === null) child.kill(); server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
