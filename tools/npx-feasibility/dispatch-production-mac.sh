#!/bin/bash
# One temporary GUI-user LaunchAgent so Keychain may ask its owner normally.
# Never unlocks a Keychain or approves a prompt; all files stay in the test root.
set -euo pipefail
root=${1:?Pass the isolated test root}
export PATH="$(cat "$HOME/.devbuddy-npx-test/tool-path.txt")"
uid=$(id -u)
if [ "$(stat -f %Su /dev/console)" != "$(id -un)" ]; then echo 'The test user has no active desktop session.' >&2; exit 1; fi
evidence=$(mktemp -d "$root/native-mac-XXXXXX")
label="me.jennarin.devbuddy-production.$(basename "$evidence")"
node - "$root" "$evidence" "$label" <<'NODE'
const fs = require('node:fs');
const [root, evidence, label] = process.argv.slice(2);
const xml = value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
fs.writeFileSync(evidence + '/job.plist', `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>Label</key><string>${xml(label)}</string>
<key>ProgramArguments</key><array><string>/bin/bash</string><string>${xml(root)}/tools/npx-feasibility/run-production-mac-desktop.command</string><string>${xml(root)}</string></array>
<key>EnvironmentVariables</key><dict><key>DEVBUDDY_PRODUCTION_NATIVE_EVIDENCE</key><string>${xml(evidence)}</string></dict>
<key>StandardOutPath</key><string>${xml(evidence)}/output.log</string><key>StandardErrorPath</key><string>${xml(evidence)}/error.log</string>
<key>RunAtLoad</key><true/></dict></plist>`);
NODE
registered=0
cleanup() { if [ "$registered" = 1 ]; then launchctl bootout "gui/$uid/$label" 2>/dev/null || true; fi; }
trap cleanup EXIT
launchctl bootstrap "gui/$uid" "$evidence/job.plist"
registered=1
echo "Started production Keychain tests in the desktop user session. Evidence: $evidence"
deadline=$((SECONDS + 2100))
while [ ! -f "$evidence/exit-code.txt" ] && [ "$SECONDS" -lt "$deadline" ]; do sleep 2; done
if [ ! -f "$evidence/exit-code.txt" ] || [ "$(cat "$evidence/exit-code.txt")" != 0 ]; then echo "Native test needs attention; inspect $evidence/error.log" >&2; exit 1; fi
echo "Production native Keychain checks passed. Evidence: $evidence"
