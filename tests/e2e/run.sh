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

# MCP over stdio with a machine token (Phase 13, C2). That is the path the plugin packages use,
# and the runner cannot reach it: it is a process the host starts, not a port on the network. So
# it runs here, from the host, the way a plugin session starts the server.
stdio_checks() {
  local log="$out/stdio.log" failures=0
  : >"$log"

  check() {
    if [ "$2" = yes ]; then echo "ok    $1" | tee -a "$log" >&2; else echo "FAIL  $1" | tee -a "$log" >&2; failures=$((failures + 1)); fi
  }

  # One exchange: initialize, then the given request as id 2, answered on stdout.
  mcp_stdio() {
    local token=$1 request=$2 extra=${3:-}
    {
      printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"e2e-stdio","version":"0"}}}'
      sleep 3
      printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
      printf '%s\n' "$request"
      sleep 8
    } | compose run --rm --no-deps -T -e DEVBUDDY_TOKEN="$token" $extra mcp --stdio 2>>"$log" | grep '"id":2' || true
  }

  local workspace actor issued token token_id
  workspace=$(awk '$1=="workspace"{print $2}' "$out/bootstrap.log")
  actor=$(awk '$1=="actor"{print $2}' "$out/bootstrap.log")

  issued=$(compose run --rm --no-deps -T migrate run issue_machine_token --actor "$actor" \
    --arguments "{\"name\":\"e2e-stdio\",\"lifetimeDays\":1,\"workspaceId\":\"$workspace\"}" 2>>"$log")
  token=$(printf '%s' "$issued" | grep -o '"token": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
  token_id=$(printf '%s' "$issued" | grep -o '"tokenId": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
  check "a machine token was minted for the administrator" "$([ -n "$token" ] && [ -n "$token_id" ] && echo yes || echo no)"

  local tools
  tools=$(mcp_stdio "$token" '{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
  check "tools/list over stdio answers" "$([ -n "$tools" ] && echo yes || echo no)"
  check "the AI surface is twenty tools" "$([ "$(printf '%s' "$tools" | grep -o '"name":"[a-z_]*"' | sort -u | wc -l | tr -d ' ')" = 20 ] && echo yes || echo no)"
  check "no human-only operation is a tool" "$(printf '%s' "$tools" | grep -qE '"name":"(grant_membership|approve_record|publish_record|delete_project|issue_password_reset)"' && echo no || echo yes)"

  local projects
  projects=$(mcp_stdio "$token" "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"list_projects\",\"arguments\":{\"workspaceId\":\"$workspace\"}}}")
  check "a tool call with the token succeeds" "$(printf '%s' "$projects" | grep -q '"isError":true' && echo no || { [ -n "$projects" ] && echo yes || echo no; })"

  local other
  other=$(mcp_stdio "$token" '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_projects","arguments":{"workspaceId":"00000000-0000-0000-0000-000000000001"}}}')
  check "the token is refused in a workspace it was not minted in" "$(printf '%s' "$other" | grep -q '"isError":true' && echo yes || echo no)"

  local actor_only
  actor_only=$(mcp_stdio "" "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"list_projects\",\"arguments\":{\"workspaceId\":\"$workspace\"}}}" "-e DEVBUDDY_ACTOR=$actor")
  check "DEVBUDDY_ACTOR names nobody: no token, no identity" "$(printf '%s' "$actor_only" | grep -q 'No identity was resolved' && echo yes || echo no)"

  compose run --rm --no-deps -T migrate run revoke_machine_token --actor "$actor" \
    --arguments "{\"tokenId\":\"$token_id\",\"workspaceId\":\"$workspace\"}" >>"$log" 2>&1
  local revoked
  revoked=$(mcp_stdio "$token" "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"list_projects\",\"arguments\":{\"workspaceId\":\"$workspace\"}}}")
  check "a revoked token is refused on the next call" "$(printf '%s' "$revoked" | grep -q 'No identity was resolved' && echo yes || echo no)"

  return "$failures"
}

echo "==> MCP over stdio with a machine token" >&2
set +e
stdio_checks
stdio_status=$?
set -e
[ "$stdio_status" -eq 0 ] || status=1

echo "==> Results in $out (report/index.html, junit.xml, stack.log, stdio.log)" >&2
exit "$status"
