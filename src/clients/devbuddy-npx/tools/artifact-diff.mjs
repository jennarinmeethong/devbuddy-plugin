// Read-only comparison of our npm-generated tarballs; never extracts files to disk.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { gunzipSync } from 'node:zlib';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function inventory(path) {
  const archive = readFileSync(path), tar = gunzipSync(archive), files = new Map();
  for (let offset = 0; offset + 512 <= tar.length;) {
    const header = tar.subarray(offset, offset + 512);
    if (header.every(byte => byte === 0)) break;
    const field = (start, length) => header.subarray(start, start + length).toString().replace(/\0.*$/s, '');
    const name = field(0, 100), prefix = field(345, 155);
    const size = Number.parseInt(field(124, 12).trim() || '0', 8);
    assert(Number.isSafeInteger(size) && size >= 0 && offset + 512 + size <= tar.length, 'Invalid tar size');
    assert(['0', '\0'].includes(String.fromCharCode(header[156])), 'Only regular-file npm tar entries are supported');
    const path = prefix ? prefix + '/' + name : name;
    assert(!files.has(path), 'Duplicate tar entry');
    files.set(path, hash(tar.subarray(offset + 512, offset + 512 + size)));
    offset += 512 + Math.ceil(size / 512) * 512;
  }
  assert(files.size > 0);
  return { artifactSha256: hash(archive), files };
}
assert.equal(process.argv.length, 4, 'Pass old and new tarball paths');
const old = inventory(process.argv[2]), current = inventory(process.argv[3]);
const added = [...current.files.keys()].filter(path => !old.files.has(path));
const removed = [...old.files.keys()].filter(path => !current.files.has(path));
const changed = [...current.files.keys()].filter(path => old.files.has(path) && old.files.get(path) !== current.files.get(path));
console.log(JSON.stringify({ oldArtifactSha256: old.artifactSha256, newArtifactSha256: current.artifactSha256,
  added, removed, changed, unchangedFileCount: current.files.size - added.length - changed.length }, null, 2));
