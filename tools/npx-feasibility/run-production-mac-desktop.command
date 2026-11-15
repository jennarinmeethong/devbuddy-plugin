#!/bin/bash
set -euo pipefail
base="$HOME/.devbuddy-npx-test"
root="${1:-$base/production-01b838f7afb5486db45cfe02219a0b24}"
evidence=${DEVBUDDY_PRODUCTION_NATIVE_EVIDENCE:-}
if [ -n "$evidence" ]; then
  trap 'status=$?; date -u +%Y-%m-%dT%H:%M:%SZ > "$evidence/finished-utc.txt"; printf "%s\n" "$status" > "$evidence/exit-code.txt"' EXIT
fi
export PATH="$(cat "$base/tool-path.txt")"
export DOTNET_ROOT="$base/tools/dotnet"
export DEVBUDDY_TEST_DOTNET_DLL="$root/src/clients/DevBuddy.Client/bin/Release/net10.0/devbuddy.dll"
export DEVBUDDY_TEST_NATIVE=1
export DEVBUDDY_NPX_TEST_TIMEOUT_MS=300000
cd "$root/src/clients/devbuddy-npx"
echo 'Production client native tests use synthetic credentials. Review Keychain prompts for the test programs.'
node tools/package-smoke.mjs > production-native-package.json
entry=$(node -e 'process.stdout.write(JSON.parse(require("fs").readFileSync("production-native-package.json", "utf8")).installedEntry)')
node tools/native-inventory.mjs "$entry" > production-native-inventory.json
if [ -n "$evidence" ]; then
  cp production-native-package.json "$evidence/package.json"
  cp production-native-inventory.json "$evidence/inventory.json"
fi
echo 'Production native/installed/.NET interchange checks passed. Reports are in the isolated client directory.'
