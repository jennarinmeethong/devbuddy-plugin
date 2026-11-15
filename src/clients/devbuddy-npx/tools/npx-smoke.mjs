// Portable packed-artifact test orchestration; not product code.
import assert from 'node:assert/strict';
import { randomUUID, createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { prepareFeasibility } from './prepare-feasibility.mjs';

const native = process.argv.slice(2).includes('--native');
assert(process.argv.slice(2).every(arg => arg === '--native'), 'Only --native is accepted');
const root = fileURLToPath(new URL('../', import.meta.url));
const nodeDirectory = dirname(process.execPath);
const npmCli = [join(nodeDirectory, 'node_modules/npm/bin/npm-cli.js'), join(nodeDirectory, '../lib/node_modules/npm/bin/npm-cli.js')].find(existsSync);
assert(npmCli, 'Use an official Node distribution including npm');
const npxCli = join(dirname(npmCli), 'npx-cli.js');
mkdirSync(join(root, 'out'), { recursive: true });
const runDirectory = mkdtempSync(join(root, 'out/npx-portable-'));
const clean = join(runDirectory, 'clean-folder-ภาษาไทย');
const cache = join(runDirectory, 'npm-cache');
mkdirSync(clean);
const env = { ...process.env, npm_config_ignore_scripts: 'true' };
function run(cli, args, cwd = clean) {
  const result = spawnSync(process.execPath, [cli, ...args], { cwd, env, encoding: 'utf8', shell: false, timeout: 120_000 });
  assert(!result.error, 'Subprocess failed to start or timed out');
  return result;
}
const feasibility = prepareFeasibility(join(runDirectory, 'feasibility'));
const pack = run(npmCli, ['pack', '--ignore-scripts', '--json', '--pack-destination', runDirectory], feasibility);
assert.equal(pack.status, 0, pack.stderr.slice(-2000));
const [manifest] = JSON.parse(pack.stdout);
assert(manifest.files.every(file => !/(^|\/)(node_modules|tests|tools|out)\/|\.env$/.test(file.path)));
writeFileSync(join(runDirectory, 'package.json'), pack.stdout);
const archive = join(runDirectory, manifest.filename);
const prefix = ['--yes', '--ignore-scripts', '--cache', cache, '--package', archive, 'devbuddy-credential-probe'];
const passes = [];
for (const pass of ['cold', 'warm']) {
  const help = run(npxCli, [...prefix, '--help']);
  assert.equal(help.status, 0, help.stderr.slice(-2000));
  assert(help.stdout.includes('DevBuddy feasibility probe'));
  writeFileSync(join(runDirectory, `${pass}-help.txt`), help.stdout);
  if (native) {
    const probe = run(npxCli, [...prefix, 'roundtrip']);
    writeFileSync(join(runDirectory, `${pass}-native.stderr.txt`), probe.stderr);
    assert.equal(probe.status, 0, probe.stderr.slice(-2000));
    const report = JSON.parse(probe.stdout);
    assert.equal(report.passed, true);
    const modules = report.nativeFiles.filter(file => file.path.endsWith('.node'));
    assert(modules.length > 0);
    for (const file of modules) {
      const path = resolve(file.path.replace(/^\\\\\?\\/, ''));
      assert(path.startsWith(cache + (process.platform === 'win32' ? '\\' : '/')), 'Loaded native module must be inside the isolated npm cache');
    }
    writeFileSync(join(runDirectory, `${pass}-native.json`), probe.stdout);
  }
  passes.push(pass);
}
for (const kind of ['stage', 'uuid']) {
  const marker = `synthetic-sensitive-argument-${randomUUID()}`;
  const args = kind === 'stage' ? [marker] : ['roundtrip', marker];
  const result = run(npxCli, [...prefix, ...args]);
  assert.equal(result.status, 1);
  assert.equal(result.stdout, '');
  assert(result.stderr.includes('DevBuddy credential probe failed'));
  assert(!result.stderr.includes(marker));
  writeFileSync(join(runDirectory, `invalid-${kind}.stderr.txt`), result.stderr);
}
const report = { passed: true, platform: process.platform, architecture: process.arch, node: process.version, passes, native, negativeCases: 'PASSED', ignoredInstallScripts: true, artifactSha256: createHash('sha256').update(readFileSync(archive)).digest('hex'), evidenceDirectory: runDirectory, sacCompatible: 'UNVERIFIED_BY_THIS_TOOL' };
writeFileSync(join(runDirectory, 'result.json'), JSON.stringify(report, null, 2));
process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
