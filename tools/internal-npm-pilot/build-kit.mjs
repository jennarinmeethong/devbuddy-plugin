import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { cpSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { basename, join, relative, resolve } from 'node:path';
import { artifactHash, version } from './prepare.mjs';

assert.equal(process.argv.length, 4, 'Pass the approved tarball and new output directory');
const source = resolve(import.meta.dirname, '../..');
const archive = resolve(process.argv[2]), output = resolve(process.argv[3]);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
assert.equal(hash(readFileSync(archive)), artifactHash);
assert(!existsSync(output), 'Build output exists; refusing overwrite');
const kit = join(output, 'pilot-kit'); mkdirSync(kit, { recursive: true });
cpSync(archive, join(kit, `devbuddy-plugin-client-${version}.tgz`));
for (const file of ['prepare.mjs', 'README.md']) cpSync(join(import.meta.dirname, file), join(kit, file));
const files = [];
function templates(from, to) {
  mkdirSync(to, { recursive: true });
  for (const name of readdirSync(from)) {
    const input = join(from, name), stat = lstatSync(input);
    assert(!stat.isSymbolicLink(), 'Plugin templates must not contain symlinks');
    if (stat.isDirectory()) templates(input, join(to, name));
    else if (name !== '.mcp.json' && name !== 'config.toml' && /\.(md|json)$/i.test(name)) cpSync(input, join(to, name));
  }
}
for (const host of ['claude', 'cowork', 'codex']) templates(join(source, 'plugins', host), join(kit, 'templates', host));
function list(folder) {
  for (const name of readdirSync(folder).sort()) {
    const path = join(folder, name);
    if (lstatSync(path).isDirectory()) list(path);
    else files.push({ path: relative(kit, path).replaceAll('\\', '/'), sha256: hash(readFileSync(path)) });
  }
}
list(kit);
writeFileSync(join(kit, 'KIT-SHA256SUMS'), files.map(file => `${file.sha256}  ${file.path}`).join('\n') + '\n');
const tarball = join(output, `devbuddy-gable-npm-pilot-kit-${version}.tar.gz`);
const packed = spawnSync('tar', ['-czf', tarball, '-C', kit, '.'], { encoding: 'utf8', shell: false, timeout: 30_000 });
assert(!packed.error && packed.status === 0, 'Could not create pilot kit archive');
writeFileSync(join(output, 'PILOT-KIT-SHA256SUMS'), `${hash(readFileSync(tarball))}  ${basename(tarball)}\n`);
const report = { prepared: true, version, kitSha256: hash(readFileSync(tarball)), clientArtifactSha256: artifactHash,
  kitArchive: tarball, files, activeHostSettingsChanged: false, finalRolloutApproved: false };
writeFileSync(join(output, 'kit-result.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
