# Shared by the release checklist scripts in this directory. Sourced, never run.
#
# Every script writes a results file of its own. A line starting "ok" or "FAIL" is a check with an
# expected value; any other line is a fact recorded for docs/operations/release-matrix.md. A script
# exits non-zero when any check failed, so a person reading the output and a runner (Phase 14, A2)
# reach the same verdict.
#
# Portable to macOS bash 3.2: no associative arrays, no mapfile, no ${var,,}.

RELEASE_TOOLS=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPOSITORY_URL=${DEVBUDDY_REPOSITORY_URL:-https://github.com/jennarinmeethong/devbuddy-plugin.git}
REGISTRY=${DEVBUDDY_REGISTRY:-ghcr.io/jennarinmeethong/devbuddy-plugin}

PASSED=0
FAILED=0
RES=${RES:-/dev/null}

note() { echo "$*" | tee -a "$RES"; }

# expect LABEL ACTUAL WANTED: WANTED is an extended regular expression the whole value must match.
expect() {
  if [[ $2 =~ ^($3)$ ]]; then
    PASSED=$((PASSED + 1))
    note "ok   $1: $2"
  else
    FAILED=$((FAILED + 1))
    note "FAIL $1: $2 (wanted $3)"
  fi
}

finish() {
  note "=== $PASSED passed, $FAILED failed"
  [ "$FAILED" -eq 0 ]
}

# "1.7.0" -> "170", for Compose project names.
compact() { echo "$1" | tr -d '.'; }

# Refuses a directory that already exists, so a run never mixes its results with an earlier one's.
fresh_dir() {
  [ -e "$1" ] && { echo "$1 already exists, refusing to reuse it" >&2; exit 1; }
  mkdir -p "$1"
  chmod 700 "$1"
}

clone_at() {
  git clone -q "$REPOSITORY_URL" "$1" && git -C "$1" -c advice.detachedHead=false checkout -q --detach "$2"
}

# A .env for a throwaway stack. Secrets are generated here and never printed.
make_env() {
  local previous
  previous=$(umask)
  umask 077
  cat > "$1" <<EOF
DEVBUDDY_DB_PASSWORD=$(openssl rand -hex 24)
DEVBUDDY_DB_IMAGE=${2:-postgres:17-alpine}
DEVBUDDY_SIGNING_KEY=$(openssl rand -base64 48 | tr -d '\n')
DEVBUDDY_EVIDENCE_ACCESS_KEY=$(openssl rand -hex 12)
DEVBUDDY_EVIDENCE_SECRET_KEY=$(openssl rand -hex 24)
DEVBUDDY_EVIDENCE_KMS_KEY=devbuddy-key:$(openssl rand -base64 32)
DEVBUDDY_PROJECTS_PATH=./projects
EOF
  umask "$previous"
}

# ports_override FILE API_PORT MCP_PORT: loopback only, so a checklist stack is never on the LAN.
ports_override() {
  cat > "$1" <<EOF
services:
  api:
    ports: !override
      - "127.0.0.1:$2:8080"
  mcp:
    ports: !override
      - "127.0.0.1:$3:8080"
EOF
}

# published_images FILE VERSION: points the four application services at the published images.
published_images() {
  cat > "$1" <<EOF
services:
  api:
    image: $REGISTRY/devbuddy-api:$2
  mcp:
    image: $REGISTRY/devbuddy-mcp:$2
  migrate:
    image: $REGISTRY/devbuddy-cli:$2
  retention:
    image: $REGISTRY/devbuddy-cli:$2
EOF
}

# The migrations a checkout carries, from its source rather than from a number typed into a script.
migration_count() {
  find "$1/src" -path '*Persistence/Migrations/*' -name '*.cs' \
    ! -name '*.Designer.cs' ! -name '*ModelSnapshot.cs' | wc -l | tr -d ' '
}
last_migration() {
  find "$1/src" -path '*Persistence/Migrations/*' -name '*.cs' \
    ! -name '*.Designer.cs' ! -name '*ModelSnapshot.cs' -exec basename {} .cs \; | sort | tail -1
}

# The AI operations a console lists: the rows whose last column is "ai". Counting lines that look
# like a bare name, as the v1.6.0 script did, printed 0 because every row has two columns.
ai_names() { awk '$NF=="ai"{print $1}'; }

wait_healthy() {
  local _
  for _ in $(seq 1 120); do
    [ "$(docker inspect -f '{{.State.Health.Status}}' "$1" 2>/dev/null)" = healthy ] && return 0
    sleep 5
  done
  return 1
}

code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

# The console prints SQL logs ahead of a JSON result; keep the last top-level object only.
last_json() { awk '/^[{]$/{buf=""; capture=1} capture{buf=buf $0 "\n"} END{printf "%s", buf}'; }

# The uid a container's main process runs as, read from the host.
uid_of() { docker top "$1" -o pid,uid | awk 'NR>1{print $2}' | sort -u | tr '\n' ' ' | sed 's/ $//'; }

# Needs C (the Compose command as an array), ERR and ADMIN from the caller.
cli() { "${C[@]}" run --rm -T migrate "$@" 2>>"$ERR"; }
op() { cli run "$1" --actor "$ADMIN" --arguments "$2" | last_json; }

# Needs WS and PROJECT from the caller.
scope_json() { jq -nc --arg ws "$WS" --arg p "$PROJECT" '{workspaceId:$ws, projectId:$p}'; }
record_args() { jq -nc --arg r "$1" --argjson s "$(scope_json)" '{recordId:$r, scope:$s}'; }
audit_args() {
  jq -nc --argjson s "$(scope_json)" --arg f "$(date -u -d '-1 day' +%Y-%m-%dT%H:%M:%SZ)" \
    --arg u "$(date -u -d '+1 day' +%Y-%m-%dT%H:%M:%SZ)" '{scope:$s, occurredFrom:$f, occurredUntil:$u}'
}
channels() { jq -r '[.entries[] | (.channel // "null")] | group_by(.) | map("\(.[0])=\(length)") | join(" ")' "$1"; }

# sign_in API EMAIL PASSWORD: prints the access token, or "null".
sign_in() {
  curl -s -X POST "$1/auth/sign-in" -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg e "$2" --arg p "$3" '{email:$e, password:$p}')" | jq -r .accessToken
}
present() { [ -n "$1" ] && [ "$1" != null ] && echo yes || echo no; }

# mcp_call TOKEN: list_projects over the MCP server's stdio transport, as a plugin calls it.
mcp_call() {
  local init inited call
  init=$(jq -nc '{jsonrpc:"2.0", id:1, method:"initialize", params:{protocolVersion:"2025-06-18", capabilities:{}, clientInfo:{name:"release-checklist", version:"0"}}}')
  inited=$(jq -nc '{jsonrpc:"2.0", method:"notifications/initialized"}')
  call=$(jq -nc --arg ws "$WS" '{jsonrpc:"2.0", id:2, method:"tools/call", params:{name:"list_projects", arguments:{workspaceId:$ws}}}')
  { echo "$init"; sleep 4; echo "$inited"; echo "$call"; sleep 15; } \
    | timeout 120 "${C[@]}" run --rm -T --no-deps -e DEVBUDDY_TOKEN="$1" mcp --stdio 2>>"$ERR" \
    | grep '"id":2'
}

same_json() { cmp -s <(jq -S "${3:-.}" "$1") <(jq -S "${3:-.}" "$2") && echo yes || echo no; }

# A host without curl (the Ubuntu arm64 guest) gets one from a container. The caller sets
# CURL_NETWORK when the stack is reachable only on its own network.
if ! command -v curl > /dev/null 2>&1; then
  curl() {
    docker run --rm --network "${CURL_NETWORK:-host}" --user "$(id -u):$(id -g)" \
      -v "${CURL_DIR:-$PWD}:${CURL_DIR:-$PWD}" -w "${CURL_DIR:-$PWD}" curlimages/curl:latest "$@"
  }
fi
