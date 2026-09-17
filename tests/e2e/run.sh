#!/usr/bin/env bash
# Runs the end-to-end suite against a stack of its own, and throws the stack away afterwards.
#
#   bash tests/e2e/run.sh                     # the whole suite
#   bash tests/e2e/run.sh --grep lifecycle    # anything after the script goes to `playwright test`
#
# What it does:
#   1. writes a throwaway env file with fresh random secrets into a temporary directory;
#   2. builds and starts docker/compose.yaml under its own Compose project name, with the overlay
#      in this directory, so an installation already running on this machine is never touched;
#   3. bootstraps a workspace and its administrator with the console, the way an operator would;
#   4. runs the suite in Playwright's image on the stack's own network;
#   5. saves the report, the JUnit file and the servers' logs, then removes the containers, the
#      volumes and the temporary directory.
#
# Environment:
#   DEVBUDDY_E2E_OUT       where results go (default tests/e2e/.out)
#   DEVBUDDY_E2E_PROJECT   the Compose project name (default devbuddy-e2e)
#   DEVBUDDY_E2E_WORKERS   parallel workers (default 4)
#   DEVBUDDY_E2E_KEEP=1    leave the stack running afterwards, to look at it; its env file path is
#                          printed, and `down -v` with the same arguments removes it
#
# Needs Docker with Compose 2.24 or later, and bash. Nothing from the repository is executed on the
# host: every build and every test runs in a container.

set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
project=${DEVBUDDY_E2E_PROJECT:-devbuddy-e2e}
keep=${DEVBUDDY_E2E_KEEP:-0}
out=${DEVBUDDY_E2E_OUT:-$here/.out}

# Git Bash on Windows rewrites arguments that look like paths, and Docker Desktop wants Windows
# paths in an env file. Neither applies elsewhere.
export MSYS_NO_PATHCONV=1
native() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi
}

random() {
  head -c "$1" /dev/urandom | base64 | tr -d '\n'
}

word() {
  random 48 | tr -d '/+=' | cut -c1-"$1"
}

work=$(mktemp -d)
mkdir -p "$work/projects"
rm -rf "$out"
mkdir -p "$out"

# The runner writes as its own account, and on Linux that is not the caller's. Both directories are
# throwaway, so they are opened to it rather than chowned, which would need root.
chmod 0777 "$work/projects" "$out"

admin_email="admin@e2e.devbuddy.test"
admin_password=$(word 32)

cat >"$work/.env" <<EOF
DEVBUDDY_DB_PASSWORD=$(word 32)
DEVBUDDY_SIGNING_KEY=$(word 48)
DEVBUDDY_EVIDENCE_ACCESS_KEY=e2e-$(word 12)
DEVBUDDY_EVIDENCE_SECRET_KEY=$(word 32)
DEVBUDDY_EVIDENCE_KMS_KEY=e2e-key:$(random 32)
DEVBUDDY_PROJECTS_PATH=$(native "$work/projects")
DEVBUDDY_E2E_OUT=$(native "$out")
DEVBUDDY_E2E_ADMIN_EMAIL=$admin_email
DEVBUDDY_E2E_ADMIN_PASSWORD=$admin_password
EOF

compose() {
  docker compose \
    --project-name "$project" \
    --env-file "$(native "$work/.env")" \
    --file "$(native "$repo/docker/compose.yaml")" \
    --file "$(native "$here/compose.e2e.yaml")" \
    --profile e2e \
    "$@"
}

tty=()
if [ ! -t 0 ] || [ ! -t 1 ]; then
  tty=(-T)
fi

finish() {
  local status=$?

  compose logs --no-color api mcp migrate >"$out/stack.log" 2>&1 || true

  if [ "$keep" = 1 ]; then
    echo "Stack '$project' left running. Env file: $work/.env" >&2
  else
    # The runner planted working copies as its own account; it removes them the same way.
    compose run --rm --no-deps "${tty[@]}" --entrypoint sh e2e -c 'rm -rf /srv/projects/*' >/dev/null 2>&1 || true
    compose down --volumes --remove-orphans >/dev/null 2>&1 || true
    rm -rf "$work"
  fi

  exit "$status"
}
trap finish EXIT

echo "==> Building the stack and the runner" >&2
compose build

echo "==> Starting the stack as '$project'" >&2
compose up --detach --wait api mcp

echo "==> Waiting for the MCP server to answer" >&2
compose run --rm --no-deps "${tty[@]}" --entrypoint node e2e -e '
  const deadline = Date.now() + 60_000;
  (async function poll() {
    try { await fetch("http://mcp:8080/", { method: "POST" }); process.exit(0); }
    catch { if (Date.now() > deadline) { console.error("MCP never answered"); process.exit(1); } }
    setTimeout(poll, 1000);
  })();
'

echo "==> Bootstrapping a workspace and its administrator" >&2
compose run --rm --no-deps "${tty[@]}" migrate bootstrap \
  --workspace-name "E2E workspace" \
  --email "$admin_email" \
  --password "$admin_password" >"$out/bootstrap.log" 2>&1 || {
  cat "$out/bootstrap.log" >&2
  exit 1
}

echo "==> Running the suite" >&2
set +e
compose run --rm --no-deps "${tty[@]}" e2e "$@"
status=$?
set -e

echo "==> Results in $out (report/index.html, junit.xml, stack.log)" >&2
exit "$status"
