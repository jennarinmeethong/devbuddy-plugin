// Actual Node processes and loopback HTTP; synthetic credentials only.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { randomUUID } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Registry } from '../dist/registry.js';
import { credentialKey, fileCredentialStore } from '../dist/file-store.js';

const root = fileURLToPath(new URL('../', import.meta.url));
mkdirSync(join(root, 'out'), { recursive: true });
const evidence = mkdtempSync(join(root, 'out/runtime-errors-'));
const home = join(evidence, 'home'), cwd = join(evidence, 'checkout');
mkdirSync(cwd);
const token = 'q'.repeat(43), workspace = randomUUID();
let mode = 429, requests = 0, child, held;
const server = createServer(async (request, response) => {
  requests++;
  let body = ''; for await (const part of request) body += part;
  const message = JSON.parse(body);
  if (typeof mode === 'number') {
    response.writeHead(mode, { 'Retry-After': '1' }); response.end('Untrusted upstream ' + token); return;
  }
  if (mode === 'disconnect') { request.socket.destroy(); return; }
  if (mode === 'doctor-timeout' || message.method === 'tools/call') {
    held = response;
    if (mode === 'sigint') child.kill('SIGINT');
    return;
  }
  response.setHeader('content-type', 'application/json');
  response.end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result: {
    protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'timeout-fixture', version: '1' },
  } }));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const registry = Registry.load(home); registry.put({ path: cwd, server: origin, workspace }); registry.save();
const store = fileCredentialStore(home); store.write(credentialKey(origin, workspace), token);
const originalRegistry = readFileSync(join(home, 'checkouts.json'), 'utf8');
const checks = [];
async function command(args, input = '', timeout = 35_000) {
  const before = requests, start = performance.now();
  child = spawn(process.execPath, [join(root, 'dist/main.js'), ...args], {
    cwd, env: { ...process.env, DEVBUDDY_HOME: home, DEVBUDDY_CREDENTIAL_STORE: 'file' },
    shell: false, stdio: ['pipe', 'pipe', 'pipe'],
  });
  let stdout = '', stderr = '';
  child.stdout.on('data', value => { stdout += value; }); child.stderr.on('data', value => { stderr += value; });
  const result = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => { child.kill(); reject(new Error('Drill deadline exceeded')); }, timeout);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('close', (code, signal) => { clearTimeout(timer); resolve({ code, signal }); });
    child.stdin.end(input);
  });
  assert(!(stdout + stderr).includes(token), 'Synthetic credential leaked');
  assert(!(stdout + stderr).includes('Untrusted upstream'), 'Upstream payload leaked');
  assert.equal(store.read(credentialKey(origin, workspace)), token, 'Stored token changed');
  assert.equal(readFileSync(join(home, 'checkouts.json'), 'utf8'), originalRegistry);
  held?.destroy(); held = undefined;
  return { ...result, stdout, stderr, requests: requests - before, elapsedMs: Math.round(performance.now() - start) };
}
try {
  for (const status of [401, 403, 404, 405, 429, 500, 503]) {
    mode = status;
    const result = await command(['doctor']);
    assert.equal(result.code, 1); assert.equal(result.requests, 1, 'Unexpected retry');
    checks.push({ case: 'doctor-http-' + status, passed: true, requests: result.requests });
  }
  mode = 'disconnect';
  const disconnected = await command(['doctor']);
  assert.equal(disconnected.code, 1); assert.equal(disconnected.requests, 1);
  checks.push({ case: 'connection-drop-no-retry', passed: true });
  mode = 'doctor-timeout';
  const timeout = await command(['token', 'set', '--server', origin, '--workspace', workspace], 'r'.repeat(43) + '\n');
  assert.equal(timeout.code, 1); assert.equal(timeout.requests, 1);
  assert(timeout.elapsedMs >= 19_000 && timeout.elapsedMs < 30_000);
  checks.push({ case: 'actual-20-second-verification-timeout-preserves-token', passed: true, elapsedMs: timeout.elapsedMs });
  const input = [
    { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18' } },
    { jsonrpc: '2.0', id: 2, method: 'tools/call', params: { name: 'list_projects', arguments: {} } },
  ].map(value => JSON.stringify(value)).join('\n') + '\n';
  if (process.platform !== 'win32') {
    mode = 'sigint';
    const cancelled = await command(['mcp-bridge'], input, 10_000);
    assert.equal(cancelled.code, 130); assert.equal(cancelled.requests, 2);
    checks.push({ case: 'actual-SIGINT-during-inflight-call', passed: true, exit: 130 });
  }
  if (process.env.DEVBUDDY_TEST_LONG_TIMEOUT === '1') {
    mode = 'bridge-timeout';
    console.error('Starting actual five-minute bridge timeout; synthetic HTTP only.');
    const timed = await command(['mcp-bridge'], input, 325_000);
    assert.equal(timed.code, 0); assert.equal(timed.requests, 2);
    const answer = timed.stdout.trim().split('\n').map(line => JSON.parse(line)).find(value => value.id === 2);
    assert.match(answer.error.message, /cancelled or timed out/);
    assert(timed.elapsedMs >= 299_000 && timed.elapsedMs < 320_000);
    checks.push({ case: 'actual-five-minute-bridge-timeout-no-retry', passed: true, elapsedMs: timed.elapsedMs });
  }
  server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
  const unavailable = await command(['doctor']); assert.equal(unavailable.code, 1);
  checks.push({ case: 'server-unavailable-preserves-registry-and-token', passed: true });
  const report = { passed: true, node: process.version, platform: process.platform, architecture: process.arch,
    syntheticOnly: true, registryAndTokenPreserved: true, checks, evidence };
  writeFileSync(join(evidence, 'result.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} finally {
  if (child && child.exitCode === null && child.signalCode === null) child.kill();
  server.closeAllConnections(); server.close();
}
