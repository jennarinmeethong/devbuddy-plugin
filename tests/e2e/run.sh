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
#   DEVBUDDY_E2E_RESTORE=0 skip the destroy-and-restore stage that runs after the suite
#   DEVBUDDY_E2E_OBSERVABILITY=1  the shipped observability overlay, for specs/observability.spec.ts
#   DEVBUDDY_E2E_GITHUB=1  a second API instance on the GitHub source mode against a stand-in
#                          GitHub, for specs/github.spec.ts
#   DEVBUDDY_E2E_EMBEDDINGS=1  pgvector, a stand-in model server and the embedding sweep, for
#                          specs/embeddings.spec.ts; off by default, so the default run tests the
#                          shipped default stack
#   DEVBUDDY_E2E_FIRST_CLICK=1  every page records its input events, and a signed-out click that
#                          sent nothing prints them as CLICK-LOST (Phase 14, C3)
#   DEVBUDDY_E2E_ZAP=1     OWASP ZAP's baseline scan and API scan against the same stack, after the
#                          suite; report-only, results in zap/ under the output directory (Phase 14, C4)
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

# The GitHub source mode (Phase 13, C3): a second API instance reading a stand-in GitHub.
github=${DEVBUDDY_E2E_GITHUB:-0}
github_repository=5b0f2a2e-7c1d-4e8f-9a3b-1c2d3e4f5a6b

# The embeddings mode (Phase 13, C1): pgvector, a stand-in model server, and the real sweep.
embeddings=${DEVBUDDY_E2E_EMBEDDINGS:-0}
overlays=(--file "$(native "$here/compose.e2e.yaml")")
if [ "$embeddings" = 1 ]; then
  overlays+=(--file "$(native "$here/compose.embeddings.yaml")")
  cat >>"$work/.env" <<EOF
DEVBUDDY_DB_IMAGE=pgvector/pgvector:pg17
DEVBUDDY_EMBEDDING_PROVIDER=SelfHosted
DEVBUDDY_EMBEDDING_ENDPOINT=http://embedder:8080/v1
DEVBUDDY_EMBEDDING_MODEL=e2e-bag-of-words
DEVBUDDY_EMBEDDING_DIMENSIONS=64
DEVBUDDY_EMBEDDING_SWEEP_EVERY=1m
DEVBUDDY_EMBEDDING_SWEEP_BUDGET=1000
EOF
fi

# The observability mode (Phase 13, C5): the shipped observability overlay, queried by a spec.
if [ "${DEVBUDDY_E2E_OBSERVABILITY:-0}" = 1 ]; then
  overlays=(--file "$(native "$repo/docker/compose.observability.yaml")" "${overlays[@]}" --file "$(native "$here/compose.observability-e2e.yaml")")
  echo "DEVBUDDY_GRAFANA_PASSWORD=$(word 24)" >>"$work/.env"
fi

if [ "$github" = 1 ]; then
  overlays+=(--file "$(native "$here/compose.github.yaml")")
  echo "DEVBUDDY_E2E_GITHUB_SETTINGS=$(native "$work/appsettings.github.json")" >>"$work/.env"
  # Written properly once the project exists; the file has to exist for Compose to parse the stack.
  echo '{}' >"$work/appsettings.github.json"
  chmod 0644 "$work/appsettings.github.json"
fi

compose() {
  docker compose \
    --project-name "$project" \
    --env-file "$(native "$work/.env")" \
    --file "$(native "$repo/docker/compose.yaml")" \
    "${overlays[@]}" \
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
  [ "${embeddings:-0}" = 1 ] && compose --profile workers logs --no-color record-embedding-sweep embedder >>"$out/stack.log" 2>&1 || true

  if [ "$keep" = 1 ]; then
    echo "Stack '$project' left running. Env file: $work/.env" >&2
  else
    # The runner planted working copies as its own account; it removes them the same way.
    compose run --rm --no-deps "${tty[@]}" --entrypoint sh e2e -c 'rm -rf /srv/projects/*' >/dev/null 2>&1 || true
    # A service behind a profile is left running by a plain `down`, holding its network and volume:
    # the embeddings mode's worker ran on for 18 hours on jmhp that way. Name every profile.
    compose --profile e2e --profile workers down --volumes --remove-orphans >/dev/null 2>&1 || true
    rm -rf "$work"
  fi

  exit "$status"
}
trap finish EXIT

echo "==> Building the stack and the runner" >&2
compose build

echo "==> Starting the stack as '$project'" >&2
services_up=(api mcp)
[ "$embeddings" = 1 ] && services_up+=(embedder)
# Nothing in the stack depends on the observability services, so they are started by name.
[ "${DEVBUDDY_E2E_OBSERVABILITY:-0}" = 1 ] && services_up+=(otel-collector tempo loki prometheus grafana)
compose up --detach --wait "${services_up[@]}"

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

suite_env=()

if [ "$github" = 1 ]; then
  echo "==> Starting a second API instance on the GitHub source mode" >&2
  g_workspace=$(awk '$1=="workspace"{print $2}' "$out/bootstrap.log")
  g_actor=$(awk '$1=="actor"{print $2}' "$out/bootstrap.log")
  g_project=$(compose run --rm --no-deps -T migrate run create_project --actor "$g_actor" \
    --arguments "{\"workspaceId\":\"$g_workspace\",\"name\":\"GitHub-backed project\"}" 2>>"$out/github.log" \
    | grep -o '"projectId": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
  [ -n "$g_project" ] || { echo "Could not create the GitHub-backed project; see github.log" >&2; exit 1; }
  cat >"$work/appsettings.github.json" <<JSON
{
  "GitHub": {
    "Mode": "GitHubApi",
    "ApiBaseUrl": "http://github-stub:8080",
    "Token": "e2e-github-token",
    "Repositories": { "$g_project/$github_repository": "octo/demo" }
  },
  "OutboundAccess": { "AllowedHosts": [ "github-stub" ], "AllowPrivateAddresses": true }
}
JSON
  compose up --detach --wait github-stub api-github >>"$out/github.log" 2>&1
  suite_env+=(-e "DEVBUDDY_E2E_GITHUB_PROJECT=$g_project" -e "DEVBUDDY_E2E_GITHUB_REPOSITORY=$github_repository")
fi

if [ "$embeddings" = 1 ]; then
  echo "==> Starting the embedding sweep as a Viewer account of its own" >&2
  e_workspace=$(awk '$1=="workspace"{print $2}' "$out/bootstrap.log")
  e_actor=$(awk '$1=="actor"{print $2}' "$out/bootstrap.log")
  e_worker=$(compose run --rm --no-deps -T migrate run create_user_account --actor "$e_actor" \
    --arguments "{\"workspaceId\":\"$e_workspace\",\"email\":\"embedding.worker@e2e.devbuddy.test\",\"displayName\":\"Embedding worker\",\"role\":\"Viewer\"}" 2>>"$out/embeddings.log" \
    | grep -o '"userId": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
  e_token=$(compose run --rm --no-deps -T migrate run issue_machine_token --actor "$e_worker" \
    --arguments "{\"name\":\"embedding sweep\",\"lifetimeDays\":1,\"workspaceId\":\"$e_workspace\"}" 2>>"$out/embeddings.log" \
    | grep -o '"token": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
  [ -n "$e_token" ] || { echo "Could not mint the worker's token; see embeddings.log" >&2; exit 1; }
  echo "DEVBUDDY_EMBEDDING_SWEEP_TOKEN=$e_token" >>"$work/.env"
  compose --profile workers up --detach embedder record-embedding-sweep >>"$out/embeddings.log" 2>&1
fi

echo "==> Running the suite" >&2
set +e
compose run --rm --no-deps "${tty[@]}" "${suite_env[@]}" e2e "$@"
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

# OWASP ZAP (Phase 14, C4), report-only. A passive baseline scan of what the API host serves, then a
# scan of the API from its own OpenAPI document, both unauthenticated. What they find never changes
# the run's status: it goes to $out/zap, and in CI to the job summary. A scan that did not finish is
# written down as not finishing, so it cannot pass for a clean report.
zap_scans() {
  local dir="$out/zap"
  mkdir -p "$dir"
  # ZAP writes as its own account, the same as the runner; the directory is throwaway.
  chmod 0777 "$dir"
  : >"$dir/status.txt"

  run_zap() {
    local name=$1 code
    shift
    set +e
    compose --profile zap run --rm --no-deps -T zap "$@" >"$dir/$name.log" 2>&1
    code=$?
    set -e
    # Both scripts exit 0 when clean, 1 for at least one FAIL, 2 for warnings only, and 3 when the
    # scan itself went wrong. Only the last is not a report.
    case $code in
      0 | 1 | 2) echo "$name: completed (exit $code)" | tee -a "$dir/status.txt" >&2 ;;
      *) echo "$name: DID NOT COMPLETE (exit $code), see zap/$name.log" | tee -a "$dir/status.txt" >&2 ;;
    esac
  }

  # -g writes every rule the scan ran with its default level. That file becomes the rules file (-c)
  # once the findings have been triaged and some are accepted.
  run_zap baseline zap-baseline.py -t http://api:8080 \
    -r baseline.html -J baseline.json -w baseline.md -g baseline-rules.conf

  # The API scan attacks what the document describes, so its length is bounded. Unauthenticated,
  # nearly every route answers 401, which is part of what this run shows.
  run_zap api zap-api-scan.py -t http://api:8080/openapi/v1.json -f openapi \
    -r api.html -J api.json -w api.md -z "-config scanner.maxScanDurationInMins=10"
}

if [ "${DEVBUDDY_E2E_ZAP:-0}" = 1 ]; then
  echo "==> OWASP ZAP: baseline and API scans, report-only" >&2
  zap_scans
fi

# The destroy-and-restore drill, on the stack the suite just filled (Phase 13, C6). Both volumes
# are destroyed, as in the release drill, and what comes back is compared with what was there.
restore_checks() {
  local log="$out/restore.log" failures=0
  : >"$log"

  check() {
    if [ "$2" = yes ]; then echo "ok    $1" | tee -a "$log" >&2; else echo "FAIL  $1" | tee -a "$log" >&2; failures=$((failures + 1)); fi
  }

  counts() {
    compose exec -T database psql -U devbuddy -d devbuddy -tAc \
      "select (select count(*) from users) || ' ' || (select count(*) from knowledge_records) || ' ' || (select count(*) from record_revisions) || ' ' || (select count(*) from evidence_objects) || ' ' || (select count(*) from audit_events where channel is not null)" 2>>"$log"
  }

  # Signs in as the administrator in the runner's network and prints the access token, or reads
  # /me with a given token and prints the status code.
  node_in_runner() {
    compose run --rm --no-deps -T --entrypoint node e2e --input-type=module -e "$1" 2>>"$log"
  }

  local workspace actor before after reference access evidence
  workspace=$(awk '$1=="workspace"{print $2}' "$out/bootstrap.log")
  actor=$(awk '$1=="actor"{print $2}' "$out/bootstrap.log")
  before=$(counts)
  echo "before: users records revisions evidence audit = $before" >>"$log"

  access=$(node_in_runner "
    const r = await fetch('http://api:8080/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email: process.env.DEVBUDDY_E2E_ADMIN_EMAIL, password: process.env.DEVBUDDY_E2E_ADMIN_PASSWORD }) });
    console.log((await r.json()).accessToken);")

  evidence=$(compose exec -T database psql -U devbuddy -d devbuddy -tAc \
    "select workspace_id || ' ' || project_id || ' ' || id || ' ' || size_bytes from evidence_objects where workspace_id = '$workspace' and redaction_state = 2 order by captured_at limit 1" 2>>"$log" | tr -d '\r')
  echo "evidence sampled: $evidence" >>"$log"

  reference=$(compose run --rm --no-deps -T migrate backup --workspace "$workspace" --actor "$actor" 2>>"$log" \
    | grep -o '"reference": *"[^"]*"' | head -1 | sed 's/.*"\([^"]*\)"$/\1/')
  check "a backup was taken ($reference)" "$([ -n "$reference" ] && echo yes || echo no)"

  compose stop api mcp retention database evidence >>"$log" 2>&1
  compose rm -f api mcp retention database evidence >>"$log" 2>&1
  docker volume rm "${project}_database" "${project}_evidence" >>"$log" 2>&1
  check "both volumes were destroyed" "$(docker volume ls -q | grep -qE "^${project}_(database|evidence)\$" && echo no || echo yes)"

  compose up --detach --wait database evidence >>"$log" 2>&1
  compose run --rm --no-deps -T migrate >>"$log" 2>&1
  compose run --rm --no-deps -T migrate restore --reference "$reference" >>"$log" 2>&1
  check "restore succeeded" "$(grep -q 'Restored ' "$log" && echo yes || echo no)"
  compose up --detach --wait api mcp >>"$log" 2>&1

  after=$(counts)
  echo "after:  users records revisions evidence audit = $after" >>"$log"
  check "accounts, records, revisions, evidence rows and channelled audit entries all came back" \
    "$([ -n "$before" ] && [ "$(echo "$before" | cut -d' ' -f1-4)" = "$(echo "$after" | cut -d' ' -f1-4)" ] && echo yes || echo no)"

  if [ -n "$evidence" ]; then
    set -- $evidence
    local downloaded
    downloaded=$(node_in_runner "
      const s = await fetch('http://api:8080/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ email: process.env.DEVBUDDY_E2E_ADMIN_EMAIL, password: process.env.DEVBUDDY_E2E_ADMIN_PASSWORD }) });
      const token = (await s.json()).accessToken;
      const r = await fetch('http://api:8080/workspaces/$1/projects/$2/evidence/$3', { headers: { authorization: 'Bearer ' + token } });
      console.log(r.status + ' ' + (await r.arrayBuffer()).byteLength);")
    check "evidence bytes came back ($downloaded, expected 200 $4)" "$([ "$downloaded" = "200 $4" ] && echo yes || echo no)"
  else
    check "there was evidence to check" no
  fi

  local me
  me=$(node_in_runner "
    const r = await fetch('http://api:8080/me', { headers: { authorization: 'Bearer $access' } });
    console.log(r.status);")
  check "an access token from before the disaster is refused (got $me)" "$([ "$me" = 401 ] && echo yes || echo no)"

  return "$failures"
}

if [ "${DEVBUDDY_E2E_RESTORE:-1}" = 1 ]; then
  echo "==> Destroying both volumes and restoring" >&2
  set +e
  restore_checks
  restore_status=$?
  set -e
  [ "$restore_status" -eq 0 ] || status=1
fi

echo "==> Results in $out (report/index.html, junit.xml, stack.log, stdio.log, restore.log, and zap/ with DEVBUDDY_E2E_ZAP=1)" >&2
exit "$status"
