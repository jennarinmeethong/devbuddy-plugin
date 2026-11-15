import { afterEach, expect, test } from "bun:test";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const roots: string[] = [];
afterEach(() => { for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true }); });

function fixture() {
  const out = fileURLToPath(new URL("../out/", import.meta.url)); mkdirSync(out, { recursive: true });
  const root = mkdtempSync(join(out, "policy-verifier-")); roots.push(root);
  const reports = {
    "policy-before.json": { available: true, enforced: true, utc: "2026-10-25T03:00:00-07:00", policies: [{ FriendlyName: "VerifiedAndReputableDesktop", IsEnforced: true }] },
    "policy-after.json": { available: true, enforced: true, utc: "2026-10-25T17:01:00+07:00", policies: [{ FriendlyName: "VerifiedAndReputableDesktop", IsEnforced: true }] },
    "run.json": { passed: true, taskResult: 0, taskRunLevel: "Limited", startedUtc: "2026-10-25T10:00:10Z", finishedUtc: "2026-10-25T10:00:40Z", policySnapshotsBracketRun: false },
    "package.json": { passed: true, native: true, artifactSha256: "a".repeat(64) },
    "inventory.json": { platform: "win32", architecture: "arm64", node: { sha256: "b".repeat(64) }, nativeFiles: [{ sha256: "c".repeat(64) }] },
  };
  function write() { for (const [name, value] of Object.entries(reports)) writeFileSync(join(root, name), JSON.stringify(value)); }
  function verify() {
    return spawnSync("node", [fileURLToPath(new URL("../tools/verify-native-evidence.mjs", import.meta.url)), root], { encoding: "utf8", shell: false, timeout: 10_000 });
  }
  return { root, reports, write, verify };
}

test("independent evidence verification compares offset timestamps in UTC and preserves the original collector", () => {
  const f = fixture(); f.write();
  const original = readFileSync(join(f.root, "run.json"), "utf8");
  expect(f.verify().status).toBe(0);
  const result = JSON.parse(readFileSync(join(f.root, "independent-policy-verification.json"), "utf8"));
  expect(result.sacEnforcedDuringRun).toBe(true);
  expect(result.originalCollectorBracketClaim).toBe(false);
  expect(readFileSync(join(f.root, "run.json"), "utf8")).toBe(original);
});

test("evidence verifier refuses missing enforcement, unbracketed times, failed tests and missing hashes", () => {
  const mutations = [
    (f: ReturnType<typeof fixture>) => { f.reports["policy-before.json"].available = false; },
    (f: ReturnType<typeof fixture>) => { f.reports["policy-after.json"].enforced = false; },
    (f: ReturnType<typeof fixture>) => { f.reports["policy-after.json"].policies[0]!.IsEnforced = false; },
    (f: ReturnType<typeof fixture>) => { f.reports["run.json"].finishedUtc = "2026-10-25T10:02:00Z"; },
    (f: ReturnType<typeof fixture>) => { f.reports["run.json"].startedUtc = "invalid"; },
    (f: ReturnType<typeof fixture>) => { f.reports["run.json"].taskResult = 1; },
    (f: ReturnType<typeof fixture>) => { f.reports["run.json"].taskRunLevel = "Highest"; },
    (f: ReturnType<typeof fixture>) => { f.reports["package.json"].native = false; },
    (f: ReturnType<typeof fixture>) => { f.reports["inventory.json"].nativeFiles[0]!.sha256 = ""; },
  ];
  for (const mutate of mutations) { const f = fixture(); mutate(f); f.write(); expect(f.verify().status).toBe(1); }
});
