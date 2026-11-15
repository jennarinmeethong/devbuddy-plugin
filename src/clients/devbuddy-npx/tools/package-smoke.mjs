import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const npm = [join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js'), join(dirname(process.execPath), '../lib/node_modules/npm/bin/npm-cli.js')].find(existsSync);
assert(npm, 'Use an official Node distribution with npm');
mkdirSync(join(root, 'out'), { recursive: true });
const evidence = mkdtempSync(join(root, 'out/package-smoke-'));
const clean = join(evidence, 'ติดตั้ง package'), cache = join(evidence, 'cache'); mkdirSync(clean);
const env = { ...process.env, npm_config_ignore_scripts: 'true', DEVBUDDY_HOME: join(evidence, 'home') };
function run(args, cwd = clean) {
  const result = spawnSync(process.execPath, [npm, ...args], { cwd, env, encoding: 'utf8', shell: false, timeout: 120_000 });
  assert(!result.error, 'npm invocation failed'); assert.equal(result.status, 0, result.stderr.slice(-2000)); return result.stdout;
}
const [packed] = JSON.parse(run(['pack', '--ignore-scripts', '--json', '--pack-destination', evidence], root));
const expected = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
assert.deepEqual(packed.files.map(file => file.path).sort(), [...expected.files, 'package.json'].sort());
assert(packed.files.every(file => !/probe|diagnostic|\.env|\.ts$|\.map$|tests\/|tools\//.test(file.path)));
const archive = join(evidence, packed.filename);
writeFileSync(join(clean, 'package.json'), '{"name":"devbuddy-install-drill","version":"0.0.0","private":true}');
run(['install', '--ignore-scripts', '--cache', cache, archive]);
const installed = join(clean, 'node_modules', expected.name, 'dist/main.js');
function node(args, offline = false) {
  const result = spawnSync(process.execPath, [installed, ...args], { cwd: clean, env: { ...env, ...(offline ? { npm_config_offline: 'true' } : {}) }, encoding: 'utf8', shell: false, timeout: 30_000 });
  assert.equal(result.status, 0, result.stderr); return result.stdout;
}
assert(node(['--help']).includes('mcp-bridge')); assert.equal(node(['--version']).trim(), expected.version);
assert(node(['list'], true).includes('No checkout'));
for (const pass of ['cold', 'warm']) {
  const args = ['exec', '--yes', '--ignore-scripts', '--cache', cache, ...(pass === 'warm' ? ['--offline'] : []), '--package', archive, '--', 'devbuddy-npx', '--help'];
  assert(run(args).includes('mcp-bridge'));
}
const prefix = join(evidence, 'global-prefix');
run(['install', '--global', '--prefix', prefix, '--ignore-scripts', '--cache', cache, archive]);
const globalEntry = process.platform === 'win32' ? join(prefix, 'node_modules', expected.name, 'dist/main.js') : join(prefix, 'lib/node_modules', expected.name, 'dist/main.js');
const globalHelp = spawnSync(process.execPath, [globalEntry, '--help'], { cwd: clean, env, encoding: 'utf8', shell: false, timeout: 30_000 });
assert.equal(globalHelp.status, 0, globalHelp.stderr); assert(globalHelp.stdout.includes('mcp-bridge'));
const sbom = run(['sbom', '--sbom-format', 'cyclonedx', '--omit', 'dev']);
assert(JSON.parse(sbom).components.some(component => component.name === 'koffi'));
writeFileSync(join(evidence, 'sbom.cdx.json'), sbom);
// Readonly commands must not load Koffi; removing its entrypoint models unavailable native code.
const { renameSync } = await import('node:fs');
const nativeEntry = join(clean, 'node_modules/koffi/index.cjs'); renameSync(nativeEntry, nativeEntry + '.disabled');
assert(node(['--help']).includes('DevBuddy')); assert(node(['list']).includes('No checkout'));
renameSync(nativeEntry + '.disabled', nativeEntry);
const integrationTimeout = process.env.DEVBUDDY_TEST_NATIVE === '1' ? 1_800_000 : 120_000;
const integration = spawnSync(process.execPath, [join(root, 'tools/client-smoke.mjs')], { cwd: root, env: { ...env, DEVBUDDY_TEST_ENTRY: installed }, encoding: 'utf8', shell: false, timeout: integrationTimeout });
assert.equal(integration.status, 0, integration.stderr); const smoke = JSON.parse(integration.stdout);
const report = { passed: true, node: process.version, platform: process.platform, architecture: process.arch, ignoredInstallScripts: true,
  localInstall: true, isolatedGlobalInstall: true, productionNpx: ['cold', 'warm-offline'], sbom: 'sbom.cdx.json',
  files: packed.files.map(file => file.path), artifactSha256: createHash('sha256').update(readFileSync(archive)).digest('hex'), installedEntry: installed, native: smoke.native, integration: smoke.checks, evidence };
writeFileSync(join(evidence, 'result.json'), JSON.stringify(report, null, 2)); process.stdout.write(JSON.stringify(report, null, 2) + '\n');
