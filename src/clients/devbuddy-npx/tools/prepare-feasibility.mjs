// Keep stage A reproducible without shipping a credential test executable to users.
import { cpSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

export function prepareFeasibility(destination) {
  const root = fileURLToPath(new URL('../', import.meta.url));
  mkdirSync(destination, { recursive: true });
  for (const name of ['probe-main', 'probe', 'credentials', 'windows', 'keychain']) {
    mkdirSync(join(destination, 'dist'), { recursive: true });
    cpSync(join(root, 'dist', name + '.js'), join(destination, 'dist', name + '.js'));
  }
  cpSync(join(root, 'README.md'), join(destination, 'README.md'));
  const manifest = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
  manifest.name = 'devbuddy-client-feasibility';
  manifest.bin = { 'devbuddy-credential-probe': 'dist/probe-main.js' };
  manifest.files = ['dist', 'README.md'];
  delete manifest.scripts;
  delete manifest.devDependencies;
  writeFileSync(join(destination, 'package.json'), JSON.stringify(manifest, null, 2));
  return destination;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) prepareFeasibility(process.argv[2]);
