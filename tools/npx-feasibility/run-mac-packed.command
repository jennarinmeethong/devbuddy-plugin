#!/bin/bash
set -euo pipefail
base="$HOME/.devbuddy-npx-test"
trap 'result=$?; printf "%s\n" "$result" > "$base/packed-desktop-exit.txt"' EXIT
export PATH="$(cat "$base/tool-path.txt")"
cd "$base/work/src/clients/devbuddy-npx"
node tools/npx-smoke.mjs --native 2> "$base/desktop-npx.stderr.txt" | tee "$base/desktop-npx.json"
