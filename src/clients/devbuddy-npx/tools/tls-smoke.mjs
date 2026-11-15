// Synthetic HTTPS/redirect/interruption checks; no system trust-store changes.
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createServer as httpsServer } from 'node:https';
import { createServer as httpServer } from 'node:http';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
mkdirSync(join(root, 'out'), { recursive: true });
const evidence = mkdtempSync(join(root, 'out/tls-smoke-'));
const key = join(evidence, 'synthetic.key'), cert = join(evidence, 'synthetic.crt');
const generated = spawnSync(process.env.DEVBUDDY_TEST_OPENSSL ?? 'openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
  '-subj', '/CN=localhost', '-addext', 'subjectAltName=IP:127.0.0.1,DNS:localhost', '-addext', 'basicConstraints=critical,CA:TRUE', '-keyout', key, '-out', cert],
{ encoding: 'utf8', shell: false, timeout: 30_000 });
assert.equal(generated.status, 0, 'OpenSSL could not generate an isolated test certificate');
let requests = 0, interrupted = 0, redirected = 0;
const secure = httpsServer({ key: readFileSync(key), cert: readFileSync(cert) }, async (request, response) => {
  requests++;
  let body = ''; for await (const chunk of request) body += chunk;
  const message = JSON.parse(body);
  response.setHeader('Content-Type', 'application/json');
  response.end(JSON.stringify({ jsonrpc: '2.0', id: message.id, result: message.method === 'initialize'
    ? { protocolVersion: '2025-06-18' } : { content: [] } }));
});
const redirect = httpServer((_request, response) => { redirected++; response.writeHead(307, { Location: `https://127.0.0.1:${secure.address().port}/mcp` }).end(); });
const interrupt = httpServer(request => { interrupted++; request.socket.destroy(); });
const servers = [secure, redirect, interrupt];
for (const server of servers) await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const workspace = '01234567-89ab-cdef-0123-456789abcdef', token = 't'.repeat(43);
async function register(name, server, trusted) {
  const home = join(evidence, name);
  const env = { ...process.env, DEVBUDDY_HOME: home, DEVBUDDY_CREDENTIAL_STORE: 'file' };
  delete env.NODE_EXTRA_CA_CERTS; delete env.NODE_TLS_REJECT_UNAUTHORIZED;
  if (trusted) env.NODE_EXTRA_CA_CERTS = cert;
  const args = [join(root, 'dist/main.js'), 'token', 'set', '--server', `${server === secure ? 'https' : 'http'}://127.0.0.1:${server.address().port}`, '--workspace', workspace];
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, args, { cwd: root, env, shell: false, stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    const timeout = setTimeout(() => { child.kill(); reject(new Error('TLS drill timed out')); }, 30_000);
    child.on('error', reject); child.stdout.on('data', chunk => { stdout += chunk; }); child.stderr.on('data', chunk => { stderr += chunk; });
    child.on('close', code => { clearTimeout(timeout); resolve({ code, stdout, stderr, home }); }); child.stdin.end(token + '\n');
  });
}
try {
  const untrusted = await register('untrusted', secure, false);
  assert.equal(untrusted.code, 1); assert(untrusted.stderr.includes('certificate')); assert.equal(requests, 0);
  assert(!existsSync(join(untrusted.home, 'tokens')));
  const trusted = await register('trusted', secure, true);
  assert.equal(trusted.code, 0, trusted.stderr); assert.equal(requests, 2); assert(existsSync(join(trusted.home, 'tokens')));
  const moved = await register('redirect', redirect, true);
  assert.equal(moved.code, 1); assert(moved.stderr.includes('redirected')); assert.equal(redirected, 1); assert.equal(requests, 2);
  assert(!existsSync(join(moved.home, 'tokens')));
  const dropped = await register('interrupted', interrupt, false);
  assert.equal(dropped.code, 1); assert(dropped.stderr.includes('could not be reached')); assert.equal(interrupted, 1);
  assert(!existsSync(join(dropped.home, 'tokens')));
  for (const output of [untrusted, trusted, moved, dropped]) assert(!(output.stdout + output.stderr).includes(token));
  const report = { passed: true, node: process.version, platform: process.platform, architecture: process.arch,
    checks: ['untrusted-CA-refused-before-retention', 'NODE_EXTRA_CA_CERTS-accepted', 'redirect-not-followed', 'connection-interruption-not-retried', 'no-token-in-errors'], evidence };
  writeFileSync(join(evidence, 'result.json'), JSON.stringify(report, null, 2)); process.stdout.write(JSON.stringify(report, null, 2) + '\n');
} finally {
  await Promise.all(servers.map(server => new Promise(resolve => { server.closeAllConnections(); server.close(resolve); })));
  rmSync(key, { force: true });
}
