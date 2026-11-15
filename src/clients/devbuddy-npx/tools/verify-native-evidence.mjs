// Independent metadata verifier. Never changes the original run/snapshot reports.
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

assert.equal(process.argv.length, 3, 'Pass one retained Windows native evidence directory');
const directory = resolve(process.argv[2]);
const read = name => JSON.parse(readFileSync(join(directory, name), 'utf8').replace(/^\uFEFF/, ''));
const before = read('policy-before.json'), after = read('policy-after.json'), run = read('run.json');
const artifact = read('package.json'), inventory = read('inventory.json');
assert(run.passed && run.taskResult === 0 && run.taskRunLevel === 'Limited');
assert(artifact.passed && artifact.native);
assert(inventory.platform === 'win32' && ['x64', 'arm64'].includes(inventory.architecture));
for (const policy of [before, after]) {
  assert(policy.available && policy.enforced);
  assert(policy.policies.some(item => item.FriendlyName === 'VerifiedAndReputableDesktop' && item.IsEnforced === true));
  assert(!policy.policies.some(item => item.FriendlyName === 'VerifiedAndReputableDesktopEvaluation' && item.IsEnforced === true));
}
const times = [before.utc, run.startedUtc, run.finishedUtc, after.utc].map(Date.parse);
assert(times.every(Number.isFinite));
assert(times[0] <= times[1] && times[1] < times[2] && times[2] <= times[3], 'SAC snapshots must bracket the test interval');
assert(/^[a-f0-9]{64}$/.test(artifact.artifactSha256));
assert(/^[a-f0-9]{64}$/.test(inventory.node.sha256));
assert(inventory.nativeFiles.length > 0 && inventory.nativeFiles.every(file => /^[a-f0-9]{64}$/.test(file.sha256)));
const report = { passed: true, sacEnforcedDuringRun: true, architecture: inventory.architecture,
  artifactSha256: artifact.artifactSha256, beforeUtc: before.utc, startedUtc: run.startedUtc,
  finishedUtc: run.finishedUtc, afterUtc: after.utc,
  originalCollectorBracketClaim: run.policySnapshotsBracketRun,
  note: 'Original evidence is preserved; this verifier compares ISO timestamps as UTC instants.' };
writeFileSync(join(directory, 'independent-policy-verification.json'), JSON.stringify(report, null, 2));
process.stdout.write(JSON.stringify(report, null, 2) + '\n');
