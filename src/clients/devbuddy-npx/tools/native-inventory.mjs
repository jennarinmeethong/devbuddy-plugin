// Test tooling only: inspect the exact installed artifact without reading a credential.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

assert(process.argv.length === 3, 'Pass the installed main.js path from the package drill');
const entry = pathToFileURL(resolve(process.argv[2]));
const { nativeCredentialStore } = await import(new URL('./credentials.js', entry));
await nativeCredentialStore();
const report = process.report.getReport();
const files = report.sharedObjects.filter(file => file.endsWith('.node'));
assert(files.length > 0, 'No loaded native module found');
const nativeFiles = files.map(file => ({ path: file, sha256: createHash('sha256').update(readFileSync(file)).digest('hex') }));
const node = { path: process.execPath, sha256: createHash('sha256').update(readFileSync(process.execPath)).digest('hex') };
process.stdout.write(JSON.stringify({ node, platform: process.platform, architecture: process.arch, nativeFiles }, null, 2) + '\n');
