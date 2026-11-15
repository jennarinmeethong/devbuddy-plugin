#!/usr/bin/env node
import { runProbe } from "./probe.js";

if (process.argv.includes("--help")) {
  process.stdout.write("DevBuddy feasibility probe (not the plugin client).\nUsage: node dist/probe-main.js [roundtrip|read-dotnet|verify-node|cleanup] [UUID]\n");
} else {
  try { process.stdout.write(`${JSON.stringify(await runProbe(process.argv.slice(2)), null, 2)}\n`); }
  catch {
    // Native loader and OS errors may contain external data. Keep stdout empty and do not echo it.
    process.stderr.write("DevBuddy credential probe failed. Inspect native signatures, SAC events and credential-store availability; no credential values were logged.\n");
    process.exitCode = 1;
  }
}
