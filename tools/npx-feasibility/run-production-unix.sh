#!/bin/sh
# Isolated test orchestration for the production npm/.NET clients, not product code.
set -eu
test_root=${1:?Pass the extracted limited bundle root}
export PATH="$(cat "$HOME/.devbuddy-npx-test/tool-path.txt")"
cd "$test_root"
dotnet build src/clients/DevBuddy.Client -c Release > dotnet-build.txt 2>&1
export DEVBUDDY_TEST_DOTNET_DLL="$test_root/src/clients/DevBuddy.Client/bin/Release/net10.0/devbuddy.dll"
cd src/clients/devbuddy-npx
bun install --frozen-lockfile --ignore-scripts > install.txt 2>&1
bun run typecheck > typecheck.txt 2>&1
bun run build > build.txt 2>&1
bun test tests > unit-tests.txt 2>&1
node tools/client-smoke.mjs > client-result.json
DEVBUDDY_TEST_PLAIN=1 node tools/client-smoke.mjs > plain-folder-result.json
node tools/package-smoke.mjs > package-result.json
printf 'Production file-store checks passed on %s/%s\n' "$(uname -s)" "$(uname -m)"
