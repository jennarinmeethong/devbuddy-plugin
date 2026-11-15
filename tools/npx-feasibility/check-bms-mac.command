#!/bin/bash
set -u
base="$HOME/.devbuddy-npx-test"
root="$base/production-01b838f7afb5486db45cfe02219a0b24"
export PATH="$(cat "$base/tool-path.txt")"
export DEVBUDDY_HOME="$HOME/.devbuddy"
unset DEVBUDDY_TOKEN_STORE
entry="$root/src/clients/devbuddy-npx/dist/main.js"
evidence=$(mktemp -d "$base/bms-readonly-XXXXXX") || exit 1
registry="$DEVBUDDY_HOME/checkouts.json"
before=$(shasum -a 256 "$registry") || exit 1
echo "Evidence: $evidence"
echo 'Read-only BMS registration and server verification. No registration/token updates.'
echo 'A Keychain access prompt may appear for Node; review it normally.'
failed=0
for folder in BMS BMS/budgetapplication-backend BMS/budgetapplication-frontend; do
  target="$HOME/Desktop/Works/$folder"
  label=${folder##*/}
  echo "Checking $folder"
  if ! cd "$target"; then
    echo 'Folder access denied or missing.'
    failed=1
    continue
  fi
  node "$entry" show --json > "$evidence/$label-registration.json" 2> "$evidence/$label-registration-error.txt"
  status=$?
  printf '%s\n' "$status" > "$evidence/$label-registration-exit.txt"
  if [ "$status" -ne 0 ]; then failed=1; fi
  node "$entry" doctor > "$evidence/$label-doctor.txt" 2>&1
  status=$?
  printf '%s\n' "$status" > "$evidence/$label-doctor-exit.txt"
  cat "$evidence/$label-doctor.txt"
  if [ "$status" -ne 0 ]; then failed=1; fi
done
after=$(shasum -a 256 "$registry") || exit 1
if [ "$before" != "$after" ]; then
  echo 'Registry changed during the test; investigate before continuing.'
  failed=1
fi
printf '%s\n' "$failed" > "$evidence/exit-code.txt"
echo "Finished with status $failed. Evidence: $evidence"
exit "$failed"
