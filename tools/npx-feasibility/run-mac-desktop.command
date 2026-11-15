#!/bin/bash
set -euo pipefail
base="$HOME/.devbuddy-npx-test"
date -u '+%Y-%m-%dT%H:%M:%SZ' > "$base/desktop-started.txt"
trap 'result=$?; printf "%s\n" "$result" > "$base/desktop-exit.txt"' EXIT
export PATH="$(cat "$base/tool-path.txt")"
export DOTNET_ROOT="$base/tools/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DEVBUDDY_NPX_TEST_TIMEOUT_MS=300000
cd "$base/work/src/clients/devbuddy-npx"
echo 'Synthetic DevBuddy credential tests: approve OS access only for these test programs.'
node tools/keychain-diagnostic.mjs | tee "$base/desktop-keychain-diagnostic.json"
node tools/compatibility.mjs ../../../tools/npx-feasibility/CredentialPeer/bin/Release/net10.0/CredentialPeer.dll 2> "$base/desktop-compatibility.stderr.txt" | tee "$base/desktop-compatibility.json"
node tools/npx-smoke.mjs --native | tee "$base/desktop-npx.json"
echo 'Desktop credential and packed-artifact probes passed.'
