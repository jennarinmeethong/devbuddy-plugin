// Test orchestration only. Product code never launches a process.
import { spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";

if (!["win32", "darwin"].includes(process.platform)) throw new Error("Native compatibility requires Windows or macOS.");
const root = fileURLToPath(new URL("../../../../", import.meta.url));
const entry = fileURLToPath(new URL("../dist/probe-main.js", import.meta.url));
const id = randomUUID();
const reports = [];
const timeout = Number(process.env.DEVBUDDY_NPX_TEST_TIMEOUT_MS ?? 120_000);
if (!Number.isInteger(timeout) || timeout < 10_000 || timeout > 300_000) throw new Error("Test timeout must be between 10000 and 300000 milliseconds.");
const peerAssembly = process.argv[2] ? resolve(process.argv[2]) : undefined;
if (process.argv.length > 3) throw new Error("Pass at most one compiled credential peer path.");
function run(command, args, env = process.env) {
  const result = spawnSync(command, args, { cwd: root, env, encoding: "utf8", shell: false, timeout });
  if (result.status !== 0) {
    // Test output contains synthetic values only, but keep diagnostics bounded anyway.
    process.stderr.write((result.stderr || result.stdout || "Probe subprocess failed.").slice(-4000));
    throw new Error(`Feasibility subprocess failed (${result.status ?? "not started"}).`);
  }
  return result.stdout;
}
function peer(stage) {
  if (peerAssembly) {
    run("dotnet", [peerAssembly, stage, id]);
    return;
  }
  run("dotnet", ["test", "tests/DevBuddy.Client.Tests", "-c", "Release", "--no-build", "--no-restore", "--filter", "FullyQualifiedName~NpxCredentialCompatibilityTests"], {
    ...process.env, DEVBUDDY_NPX_PROBE_ID: id, DEVBUDDY_NPX_PROBE_STAGE: stage,
  });
}
function node(stage) { reports.push(JSON.parse(run(process.execPath, [entry, stage, id]))); }
let failed = false;
try {
  node("roundtrip");
  peer("seed");
  node("read-dotnet");
  peer("verify");
  node("verify-node");
} catch {
  failed = true;
} finally {
  try { peer("cleanup"); } catch { failed = true; }
}
process.stdout.write(`${JSON.stringify({ credentialCompatibility: failed ? "FAILED" : "PASSED", sacCompatible: "UNVERIFIED", reports }, null, 2)}\n`);
if (failed) process.exitCode = 1;
