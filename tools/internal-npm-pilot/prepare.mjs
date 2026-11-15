// Internal pilot setup utility, not part of the production MCP client.
// Generates reviewed configuration files; never edits active host settings or credentials.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { cpSync, existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';

export const version = '1.14.0-next.0';
export const artifactHash = '99f0c5ea9e923e924c0df27d40583368493f0fbf2533e8af22f26f73f9d1d10f';
const here = dirname(fileURLToPath(import.meta.url));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function safePath(path) {
  assert(typeof path === 'string' && isAbsolute(path) && !/[\x00-\x1f\x7f]/.test(path), 'Use absolute paths without control characters');
  return path;
}
export function profiles({ node, entry, checkout, home }) {
  [node, entry, checkout, home].forEach(safePath);
  const config = { command: node, args: ['--use-system-ca', entry, 'mcp-bridge', '--dir', checkout], env: { DEVBUDDY_HOME: home } };
  const q = JSON.stringify;
  return {
    json: { mcpServers: { devbuddy: config } },
    codex: `[mcp_servers.devbuddy]\ncommand = ${q(node)}\nargs = ${q(config.args)}\nstartup_timeout_sec = 30\ntool_timeout_sec = 320\n\n[mcp_servers.devbuddy.env]\nDEVBUDDY_HOME = ${q(home)}\n`,
  };
}
export function prepare({ archive, checkout, output, home = join(homedir(), '.devbuddy'), templates }) {
  assert.equal(Number(process.versions.node.split('.')[0]), 24, 'Use Node 24 with npm');
  [archive, checkout, output, home, templates].forEach(safePath);
  assert(existsSync(checkout) && statSync(checkout).isDirectory(), 'Checkout must be an existing folder');
  assert(!existsSync(output), 'Output already exists; use a new directory rather than overwriting');
  assert.equal(hash(readFileSync(archive)), artifactHash, 'Archive differs from the tested pilot artifact');
  const npm = [join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js'),
    join(dirname(process.execPath), '../lib/node_modules/npm/bin/npm-cli.js')].find(existsSync);
  assert(npm, 'npm CLI not found beside Node; use a Node distribution with npm');
  mkdirSync(output, { recursive: true, mode: 0o700 });
  const client = join(output, 'client'); mkdirSync(client);
  const packed = join(client, `devbuddy-plugin-client-${version}.tgz`);
  cpSync(archive, packed);
  writeFileSync(join(client, 'package.json'), '{"name":"devbuddy-internal-pilot-install","version":"0.0.0","private":true}\n');
  const install = spawnSync(process.execPath, [npm, 'install', '--ignore-scripts', '--no-audit', '--no-fund', '--save-exact', packed],
    { cwd: client, encoding: 'utf8', shell: false, timeout: 180_000 });
  assert(!install.error && install.status === 0, 'npm install failed; check approved registry/proxy/CA access (no host settings changed)');
  const entry = join(client, 'node_modules/devbuddy-plugin-client/dist/main.js');
  const installed = JSON.parse(readFileSync(join(client, 'node_modules/devbuddy-plugin-client/package.json'), 'utf8'));
  assert.equal(installed.version, version);
  const checked = spawnSync(process.execPath, [entry, '--version'], { encoding: 'utf8', shell: false, timeout: 15_000 });
  assert.equal(checked.status, 0); assert.equal(checked.stdout.trim(), version);
  const generated = profiles({ node: process.execPath, entry, checkout, home });
  for (const host of ['claude', 'cowork']) {
    const target = join(output, host + '-plugin');
    cpSync(join(templates, host), target, { recursive: true, errorOnExist: true, force: false });
    writeFileSync(join(target, '.mcp.json'), JSON.stringify(generated.json, null, 2) + '\n');
    const manifest = join(target, '.claude-plugin/plugin.json');
    const metadata = JSON.parse(readFileSync(manifest, 'utf8'));
    metadata.name = `devbuddy-gable-${host}-npm-pilot`;
    metadata.version = version;
    metadata.description = 'Internal G-Able npm pilot. ' + metadata.description;
    writeFileSync(manifest, JSON.stringify(metadata, null, 2) + '\n');
  }
  mkdirSync(join(output, 'codex'));
  cpSync(join(templates, 'codex/AGENTS.md'), join(output, 'codex/AGENTS.md'));
  writeFileSync(join(output, 'codex/config.toml'), generated.codex);
  writeFileSync(join(output, 'claude-desktop.json'), JSON.stringify(generated.json, null, 2) + '\n');
  const result = { prepared: true, version, artifactSha256: artifactHash, node: process.execPath, installedEntry: entry,
    checkout, clientHome: home, output, transport: 'stdio', systemCa: true, activeHostSettingsChanged: false,
    devbuddyCredentialsReadOrChanged: false, devbuddyRegistryReadOrChanged: false, rollback: 'Restore the previous host configuration; keep shared registry and tokens.' };
  writeFileSync(join(output, 'setup-result.json'), JSON.stringify(result, null, 2) + '\n');
  writeFileSync(join(output, 'ACTIVATE.md'), `# Internal npm pilot configuration\n\nPrepared for ${version}. Not activated automatically.\n\n` +
    'Back up the active host configuration before opting into the pilot. Use claude-plugin/cowork-plugin in the appropriate local plugin installation flow, or merge only the devbuddy entry from claude-desktop.json or codex/config.toml. Keep unrelated MCP entries. Disable any duplicate DevBuddy integration in the pilot session so results identify this client. Review host permissions and Keychain prompts normally.\n\n' +
    'These files contain machine-specific absolute paths: keep this output directory and Node installation at their current locations. No PATH lookup, npx network download, or Windows .cmd shim is used when connecting. Node uses the existing system CA store; no trust settings are changed.\n\n' +
    'Register the checkout through the installed client if needed; enter tokens only at its hidden prompt. For rollback restore the old host configuration and restart the session. Keep the registry/tokens: both clients share them.\n');
  return result;
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = {};
    for (let i = 2; i < process.argv.length; i += 2) {
      const key = process.argv[i];
      assert(['--archive', '--checkout', '--output', '--home', '--templates'].includes(key) && process.argv[i + 1] && !args[key],
        'Usage: node prepare.mjs --checkout <folder> --output <new-folder> [--archive <tgz>] [--home <client-home>] [--templates <folder>]');
      args[key] = process.argv[i + 1];
    }
    assert(args['--checkout'] && args['--output'], 'Both --checkout and --output are required');
    const templates = args['--templates'] ? resolve(args['--templates']) : existsSync(join(here, 'templates')) ? join(here, 'templates') : resolve(here, '../../plugins');
    const result = prepare({ archive: resolve(args['--archive'] ?? join(here, `devbuddy-plugin-client-${version}.tgz`)),
      checkout: resolve(args['--checkout']), output: resolve(args['--output']), home: resolve(args['--home'] ?? join(homedir(), '.devbuddy')), templates });
    console.log(JSON.stringify(result, null, 2));
  } catch (error) { console.error('Pilot preparation failed: ' + (error instanceof assert.AssertionError ? error.message : 'check paths, permissions and dependencies')); process.exitCode = 1; }
}
