// Real Node integration over HTTP and stdio, with synthetic data in a unique home.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
mkdirSync(join(root, 'out'), { recursive: true });
const evidence = mkdtempSync(join(root, 'out/client-smoke-'));
const home = join(evidence, 'home'), cwd = join(evidence, 'งาน ทดสอบ');
mkdirSync(cwd);
const plainFolder = process.env.DEVBUDDY_TEST_PLAIN === '1';
if (!plainFolder) mkdirSync(join(cwd, '.git'));
const native = process.env.DEVBUDDY_TEST_NATIVE === '1';
const workspace = native ? randomUUID() : '01234567-89ab-cdef-0123-456789abcdef';
let accepted = 'a'.repeat(43);
const requests = [];
const server = createServer(async (request, response) => {
  if (request.headers.authorization !== `Bearer ${accepted}`) { response.writeHead(401).end(); return; }
  let body = ''; for await (const chunk of request) body += chunk;
  const message = JSON.parse(body); requests.push(message);
  if (message.id === undefined) { response.writeHead(202).end(); return; }
  if (message.method === 'initialize') {
    response.setHeader('content-type', 'application/json');
    response.end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result: { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'synthetic', version: '1' }, instructions: 'Synthetic instructions.' } })); return;
  }
  if (message.method === 'tools/list') {
    response.setHeader('content-type', 'application/json');
    response.end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result: { tools: [{ name: 'list_projects', inputSchema: { type: 'object', properties: { workspaceId: { type: 'string' } }, required: ['workspaceId'] } }] } })); return;
  }
  assert.equal(message.params.arguments.workspaceId, workspace);
  response.setHeader('content-type', 'text/event-stream');
  response.write('data: ' + JSON.stringify({ jsonrpc: '2.0', method: 'notifications/progress', params: { text: 'กำลังทดสอบ' } }) + '\r\n\r\n');
  response.end('data: ' + JSON.stringify({ jsonrpc: '2.0', id: message.id, result: { content: [{ type: 'text', text: '[]' }] } }) + '\n\n');
});
await new Promise(resolveListen => server.listen(0, '127.0.0.1', resolveListen));
const origin = `http://127.0.0.1:${server.address().port}`;
const env = { ...process.env, DEVBUDDY_HOME: home, DEVBUDDY_CREDENTIAL_STORE: 'file' };
if (native) delete env.DEVBUDDY_CREDENTIAL_STORE;
const entry = process.env.DEVBUDDY_TEST_ENTRY ?? join(root, 'dist/main.js');
const commandTimeout = Number(process.env.DEVBUDDY_NPX_TEST_TIMEOUT_MS ?? 30_000);
assert(Number.isInteger(commandTimeout) && commandTimeout >= 10_000 && commandTimeout <= 300_000, 'Invalid test timeout');
function command(args, input = '', executable = process.execPath, prefix = [entry]) {
  return new Promise((resolveCommand, reject) => {
    const child = spawn(executable, [...prefix, ...args], { cwd, env, shell: false, stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error('CLI integration timed out')); }, commandTimeout);
    child.stdout.on('data', chunk => { stdout += chunk; }); child.stderr.on('data', chunk => { stderr += chunk; });
    child.on('error', reject); child.on('close', code => { clearTimeout(timer); resolveCommand({ code, stdout, stderr }); });
    child.stdin.end(input);
  });
}
const checks = [];
try {
  let result = await command(['register', '--server', origin, '--workspace', workspace, '--label', 'Synthetic'], accepted + '\n');
  assert.equal(result.code, 0, result.stderr); checks.push(native ? 'register/verify/native-store' : 'register/verify/file-store');
  if (process.platform !== 'win32' && !native) {
    for (const directory of [home, join(home, 'tokens')]) assert.equal(statSync(directory).mode & 0o777, 0o700);
    assert.equal(statSync(join(home, 'checkouts.json')).mode & 0o777, 0o600);
    for (const file of readdirSync(join(home, 'tokens'))) assert.equal(statSync(join(home, 'tokens', file)).mode & 0o777, 0o600);
    checks.push('Unix-directory-0700/file-0600');
  }
  result = await command(['show', '--json']); assert.equal(result.code, 0); assert.equal(JSON.parse(result.stdout).workspaceId, workspace);
  result = await command(['mcp-headers', '--url', origin + '/mcp']); assert.equal(result.code, 0); assert.equal(JSON.parse(result.stdout).Authorization, `Bearer ${accepted}`);
  result = await command(['mcp-headers', '--url', 'https://wrong.invalid/mcp']); assert.equal(result.code, 3); assert.equal(result.stdout, ''); checks.push('header/origin');
  const calls = [{ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18' } },
    { jsonrpc: '2.0', method: 'notifications/initialized' }, { jsonrpc: '2.0', id: 2, method: 'tools/list' },
    { jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'list_projects', arguments: { workspaceId: 'untrusted' } } }];
  result = await command(['mcp-bridge'], calls.map(message => JSON.stringify(message)).join('\n') + '\n');
  assert.equal(result.code, 0, result.stderr);
  const messages = result.stdout.trim().split('\n').map(line => JSON.parse(line));
  assert(messages.find(message => message.id === 1).result.instructions.includes('Synthetic instructions.'));
  assert(!Object.hasOwn(messages.find(message => message.id === 2).result.tools[0].inputSchema.properties, 'workspaceId'));
  assert(messages.some(message => message.method === 'notifications/progress'));
  assert(messages.some(message => message.id === 3 && message.result)); checks.push('bridge/initialize/SSE/EOF/workspace');
  accepted = 'b'.repeat(43);
  result = await command(['token', 'set', '--server', origin, '--workspace', workspace], accepted + '\n'); assert.equal(result.code, 0);
  result = await command(['doctor']); assert.equal(result.code, 0, result.stderr); checks.push('replacement/doctor');
  const dotnet = process.env.DEVBUDDY_TEST_DOTNET_DLL;
  if (dotnet) {
    const peer = (args, input) => command(args, input, 'dotnet', [resolve(dotnet)]);
    result = await peer(['show', '--json']); assert.equal(result.code, 0); assert.equal(JSON.parse(result.stdout).workspaceId, workspace);
    result = await peer(['mcp-headers', '--url', origin + '/mcp']); assert.equal(result.code, 0); assert.equal(JSON.parse(result.stdout).Authorization, `Bearer ${accepted}`);
    result = await peer(['doctor']); assert.equal(result.code, 0, result.stderr);
    result = await peer(['update', '--label', 'Written by .NET']); assert.equal(result.code, 0, result.stderr);
    result = await command(['list']); assert(result.stdout.includes('Written by .NET'));
    accepted = 'c'.repeat(43);
    result = await peer(['token', 'set', '--server', origin, '--workspace', workspace], accepted + '\n'); assert.equal(result.code, 0, result.stderr);
    result = await command(['doctor']); assert.equal(result.code, 0, result.stderr); checks.push(native ? 'NET/Node-registry-and-native-token-both-directions' : 'NET/Node-registry-and-file-token-both-directions');
    // Exercise an actual return to the .NET transport, not only its registry/header commands.
    result = await peer(['mcp-bridge'], calls.map(value => JSON.stringify(value)).join('\n') + '\n');
    assert.equal(result.code, 0, result.stderr);
    const peerMessages = result.stdout.trim().split('\n').map(line => JSON.parse(line));
    assert(peerMessages.some(message => message.id === 3 && message.result));
    accepted = 'd'.repeat(43);
    result = await command(['token', 'set', '--server', origin, '--workspace', workspace], accepted + '\n'); assert.equal(result.code, 0);
    result = await peer(['doctor']); assert.equal(result.code, 0, result.stderr);
    checks.push('rollback-to-NET-bridge-and-NET-doctor-after-new-Node-token');
  }
  result = await command(['token', 'remove', '--server', origin, '--workspace', workspace]); assert.equal(result.code, 1);
  result = await command(['unregister']); assert.equal(result.code, 0);
  result = await command(['show', '--json']); assert.equal(result.code, 2);
  result = await command(['token', 'remove', '--server', origin, '--workspace', workspace]); assert.equal(result.code, 0); checks.push('unregister/remove');
  const report = { passed: true, node: process.version, platform: process.platform, architecture: process.arch, native, plainFolder, checks, requests: requests.length, evidence };
  writeFileSync(join(evidence, 'result.json'), JSON.stringify(report, null, 2));
  process.stdout.write(JSON.stringify(report, null, 2) + '\n');
} finally {
  try {
    if (native) {
      const { nativeCredentialStore } = await import(new URL('./credentials.js', pathToFileURL(entry)));
      (await nativeCredentialStore()).delete(`devbuddy:${origin}/${workspace}`);
    }
  } finally { await new Promise(resolveClose => server.close(resolveClose)); }
}
