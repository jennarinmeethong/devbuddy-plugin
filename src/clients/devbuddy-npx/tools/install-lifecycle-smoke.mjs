import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Windows-only drill: leave the OS command interpreter available for npm lifecycle hooks,
// while excluding developer toolchains from PATH. Never changes the machine's PATH.
assert.equal(process.platform, 'win32');
const root = fileURLToPath(new URL('../', import.meta.url));
mkdirSync(join(root, 'out'), { recursive: true });
const evidence = mkdtempSync(join(root, 'out/install-lifecycle-'));
const npm = join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js');
const env = { ...process.env };
for (const name of Object.keys(env)) {
  if (/^path$/i.test(name) || /^npm_config_/i.test(name)) delete env[name];
}
env.PATH = [dirname(process.execPath), join(process.env.SystemRoot, 'System32')].join(';');
env.npm_config_userconfig = join(evidence, 'empty.npmrc');
env.npm_config_cache = join(evidence, 'cache');
env.npm_config_ignore_scripts = 'false';
writeFileSync(env.npm_config_userconfig, '');
const absent = ['cmake', 'cl', 'cc', 'gcc', 'g++', 'clang', 'ninja', 'make'];
for (const command of absent) {
  const result = spawnSync(command, ['--version'], { env, shell: false });
  assert.equal(result.error?.code, 'ENOENT', `${command} must be unavailable`);
}
function npmRun(args, cwd) {
  const result = spawnSync(process.execPath, [npm, ...args], {
    cwd, env, shell: false, encoding: 'utf8', timeout: 180_000,
  });
  assert(!result.error, result.error?.message);
  assert.equal(result.status, 0, result.stderr);
  return result;
}
const [packed] = JSON.parse(npmRun(['pack', '--ignore-scripts', '--json', '--pack-destination', evidence], root).stdout);
const archive = join(evidence, packed.filename);
const clean = join(evidence, 'ติดตั้ง scripts enabled');
mkdirSync(clean);
writeFileSync(join(clean, 'package.json'), '{"name":"devbuddy-lifecycle-drill","version":"0.0.0","private":true}');
const install = npmRun(['install', '--foreground-scripts', '--ignore-scripts=false', '--no-audit', '--no-fund', archive], clean);
writeFileSync(join(evidence, 'install.log'), install.stdout + install.stderr);
assert.match(install.stdout, /koffi@3\.3\.3 install/);
assert(!/rebuilding from source|Running configuration|Running build/.test(install.stdout + install.stderr));
const load = spawnSync(process.execPath, ['-e', 'require("koffi"); console.log("native loaded")'], {
  cwd: clean, env, shell: false, encoding: 'utf8', timeout: 30_000,
});
assert.equal(load.status, 0, load.stderr);
assert.equal(load.stdout.trim(), 'native loaded');
const report = { passed: true, platform: process.platform, architecture: process.arch,
  node: process.version, scriptsEnabled: true, unavailableToolchains: absent,
  prebuiltNativeLoaded: true, artifactSha256: createHash('sha256').update(readFileSync(archive)).digest('hex'), evidence };
writeFileSync(join(evidence, 'result.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
