#!/usr/bin/env bash
# Release checklist, part 3, before the tag: upgrade an installation of the previous release,
# running its published images and its tag's Compose file, to the commit to be tagged built from
# source. The same volumes and the same .env carry across; only the Compose file and the images
# change. Runs on amd64 and on arm64 alike: the published images are multi-architecture and the
# new ones are built natively. Compose project devbuddy-up<version> on 127.0.0.1:28080/28081.
# Synthetic data only; the password is generated here and never printed.
#
#   upgrade.sh VERSION PREVIOUS COMMIT
#
# DEVBUDDY_RELEASE_WORK  as for setup-and-suite.sh. Creates $WORK/v$VERSION-upgrade-<arch>.
#
# A release adds a step by writing a function and adding its name to BEFORE_UPGRADE,
# DURING_UPGRADE or AFTER_UPGRADE at the bottom.
set -uo pipefail
source "$(dirname "$0")/lib.sh"

VERSION=${1:?version, e.g. 1.7.0}
PREVIOUS=${2:?previous release, e.g. 1.6.0}
COMMIT=${3:?commit to be tagged}
WORK=${DEVBUDDY_RELEASE_WORK:-$HOME/devbuddy-release}
ARCH=$(uname -m)
BASE=$WORK/v$VERSION-upgrade-$ARCH
R=$BASE/repo
U=$BASE/previous
OUT=$BASE/results
D=$OUT/data

fresh_dir "$BASE"
mkdir -p "$D"
chmod 700 "$OUT" "$D"
RES=$OUT/part3-results.txt
ERR=$OUT/part3-stderr.log
: > "$RES"
: > "$ERR"

note "=== v$PREVIOUS to v$VERSION on $ARCH ($(uname -sr))"
clone_at "$R" "$COMMIT" || { note "clone failed"; exit 1; }
clone_at "$U" "v$PREVIOUS" || { note "clone of v$PREVIOUS failed"; exit 1; }
note "new $(git -C "$R" log --oneline -1); previous $(git -C "$U" log --oneline -1)"
make_env "$U/docker/.env"
ports_override "$OUT/ports.yaml" 28080 28081
published_images "$OUT/published-$PREVIOUS.yaml" "$PREVIOUS"

P=devbuddy-up$(compact "$VERSION")
OLD=(docker compose -p "$P" --env-file "$U/docker/.env" -f "$U/docker/compose.yaml" -f "$OUT/published-$PREVIOUS.yaml" -f "$OUT/ports.yaml")
NEW=(docker compose -p "$P" --env-file "$U/docker/.env" -f "$R/docker/compose.yaml" -f "$OUT/ports.yaml")
C=("${OLD[@]}")
API=http://127.0.0.1:28080
if ! command -v curl > /dev/null 2>&1; then
  # The container curl in lib.sh reaches the stack on its own network, by service name.
  API=http://api:8080
  CURL_NETWORK=${P}_internal
  CURL_DIR=$D
fi
cd "$D"

db() { docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc "$1"; }
draft() {
  op create_draft "$(jq -nc --arg w "$WI" --arg t "$1" --argjson s "$(scope_json)" --arg now "$(date -u +%Y-%m-%dT%H:%M:%SZ)" --arg v "$PREVIOUS" '{workItemId:$w, kind:"Decision", title:$t, body:("Written on v" + $v + " before the upgrade."), provenance:{sourceKind:"HumanAuthored", sourceLocator:"upgrade-check", author:"upgrade-admin", recordedAt:$now}, scope:$s}')" | jq -r .recordId
}
download() {
  curl -s -o "$2" -w '%{http_code}' -H "Authorization: Bearer $ACCESS" "$API/workspaces/$WS/projects/$PROJECT/evidence/$1"
}

########## the previous release, as published ##########

previous_release() {
  note "=== v$PREVIOUS, published images"
  "${C[@]}" pull api mcp migrate retention >> "$ERR" 2>&1
  expect "pull exit" "$?" 0
  # v1.6.0's evidence store is FROM quay.io/minio/minio, which has refused anonymous pulls since
  # 2026-09-24, so this build only succeeds on a machine that still holds that image.
  "${C[@]}" build evidence >> "$ERR" 2>&1
  expect "evidence build of v$PREVIOUS" "$?" 0
  "${C[@]}" up -d --no-build >> "$ERR" 2>&1
  wait_healthy "$P-api-1"
  expect "api health on v$PREVIOUS" "$(docker inspect -f '{{.State.Health.Status}}' "$P-api-1")" healthy
  expect "api image" "$(docker inspect -f '{{.Config.Image}}' "$P-api-1")" ".*devbuddy-api:$PREVIOUS"
  expect "migrations on v$PREVIOUS" "$(db 'select count(*) from "__EFMigrationsHistory"')" "$(migration_count "$U")"
}

seed() {
  EMAIL=upgrade-admin@devbuddy.test
  PASSWORD=$(openssl rand -hex 20)
  local boot hash
  boot=$(cli bootstrap --workspace-name "Upgrade to v$VERSION" --email "$EMAIL" --password "$PASSWORD" --project-name "Upgrade project")
  WS=$(awk '$1=="workspace"{print $2}' <<<"$boot")
  PROJECT=$(awk '$1=="project"{print $2}' <<<"$boot")
  ADMIN=$(awk '$1=="actor"{print $2}' <<<"$boot")
  [ -n "$WS" ] && [ -n "$PROJECT" ] && [ -n "$ADMIN" ] || { expect bootstrap failed ok; tail -20 "$ERR"; finish; exit 1; }
  WI=$(op create_work_item "$(jq -nc --argjson s "$(scope_json)" --arg v "$PREVIOUS" '{key:"UP-1", type:"Develop", title:"Upgrade check", goal:("Carry data from v" + $v), scope:$s}')" | jq -r .workItemId)
  PUB=$(draft "Published before the upgrade")
  hash=$(op view_record_history "$(record_args "$PUB")" | jq -r '.revisions[-1].contentHash')
  op submit_for_approval "$(record_args "$PUB")" > /dev/null
  op approve_record "$(jq -nc --arg r "$PUB" --arg h "$hash" --argjson s "$(scope_json)" '{recordId:$r, approvedContentHash:$h, scope:$s}')" > /dev/null
  expect "published on v$PREVIOUS" "$(op publish_record "$(record_args "$PUB")" | jq -r .status)" Published
  DRAFT=$(draft "Still a draft at the upgrade")

  ACCESS=$(sign_in "$API" "$EMAIL" "$PASSWORD")
  ACCESS_OLD=$ACCESS
  head -c 4096 /dev/urandom > "$D/before.bin"
  curl -s -o capture-before.json -X POST -H "Authorization: Bearer $ACCESS" \
    -F "file=@before.bin;type=application/octet-stream" -F "description=before" \
    "$API/workspaces/$WS/projects/$PROJECT/evidence"
  EV=$(jq -r .evidenceId "$D/capture-before.json")
  expect "evidence captured on v$PREVIOUS" "$(present "$EV")" yes
  op enable_project_ai_access "$(jq -nc --argjson s "$(scope_json)" '{scope:$s}')" > /dev/null
  TOKEN=$(op issue_machine_token "$(jq -nc --arg ws "$WS" '{name:"upgrade-plugin", lifetimeDays:30, workspaceId:$ws}')" | jq -r .token)
  mcp_call "$TOKEN" > "$D/mcp-before.json"
  op get_record "$(record_args "$PUB")" > "$D/record-before.json"
  op view_record_history "$(record_args "$PUB")" > "$D/history-before.json"
  op read_audit_history "$(audit_args)" > "$D/audit-before.json"
  BEFORE_COUNT=$(jq '.entries | length' "$D/audit-before.json")
  note "audit entries on v$PREVIOUS: $BEFORE_COUNT"
}

########## the upgrade ##########

upgrade() {
  note "=== upgrade: v$VERSION Compose file and images built from $(git -C "$R" rev-parse --short HEAD)"
  C=("${NEW[@]}")
  "${C[@]}" build >> "$ERR" 2>&1
  expect "build exit" "$?" 0
  SINCE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  "${C[@]}" up -d --no-build >> "$ERR" 2>&1
  expect "up exit" "$?" 0
  wait_healthy "$P-api-1"
  expect "api health after the upgrade" "$(docker inspect -f '{{.State.Health.Status}}' "$P-api-1")" healthy
  expect "migrate exit" "$(docker inspect -f '{{.State.ExitCode}}' "$P-migrate-1")" 0
  note "migrate log: $(docker logs --since "$SINCE" "$P-migrate-1" 2>&1 | grep -oiE 'Applying [0-9]+ migration\(s\)|No migrations' | sort -u | tr '\n' ' ')"
  expect "migrations now" "$(db 'select count(*) from "__EFMigrationsHistory"')" "$(migration_count "$R")"
  expect "last migration" "$(db 'select max("MigrationId") from "__EFMigrationsHistory"')" "$(last_migration "$R")"
  local s
  for s in api mcp retention database evidence; do
    expect "$s running" "$(docker inspect -f '{{.State.Status}}' "$P-$s-1")" running
  done
  for s in api mcp retention; do
    expect "$s uid" "$(uid_of "$P-$s-1")" 1654
  done
  expect "database uid" "$(uid_of "$P-database-1")" 70
  expect "evidence uid" "$(uid_of "$P-evidence-1")" 1000
  expect "evidence errors since the upgrade" "$(docker logs --since "$SINCE" "$P-evidence-1" 2>&1 | grep -ciE 'denied|faulty|error')" 0
}

########## after ##########

after_upgrade() {
  note "=== after"
  ACCESS=$(sign_in "$API" "$EMAIL" "$PASSWORD")
  expect "sign-in with the same password" "$(present "$ACCESS")" yes
  op get_record "$(record_args "$PUB")" > "$D/record-after.json"
  op view_record_history "$(record_args "$PUB")" > "$D/history-after.json"
  expect "published record identical" "$(same_json "$D/record-before.json" "$D/record-after.json")" yes
  expect "history identical" "$(same_json "$D/history-before.json" "$D/history-after.json")" yes
  cli run get_record --actor "$ADMIN" --arguments "$(record_args "$DRAFT")" > /dev/null 2>&1
  expect "never-published draft with no revision number is refused" "$?" "[1-9][0-9]*"
  expect "the same draft by revision 1" "$(op get_record "$(jq -nc --arg r "$DRAFT" --argjson s "$(scope_json)" '{recordId:$r, revisionNumber:1, scope:$s}')" | jq -r .revisionNumber)" 1

  op read_audit_history "$(audit_args)" > "$D/audit-after.json"
  note "audit entries after: $(jq '.entries | length' "$D/audit-after.json"); by channel: $(channels "$D/audit-after.json")"
  expect "entries written on v$PREVIOUS still present" \
    "$(comm -12 <(jq -r '.entries[].id' "$D/audit-before.json" | sort) <(jq -r '.entries[].id' "$D/audit-after.json" | sort) | wc -l | tr -d ' ')" "$BEFORE_COUNT"
  expect "channel filter Human returns only Human" \
    "$(op read_audit_history "$(audit_args | jq -c '. + {channel:"Human"}')" | jq -c '[.entries[] | .channel] | unique')" '\["Human"\]'

  expect "evidence from v$PREVIOUS" "$(download "$EV" download-before.bin)" 200
  expect "  byte-identical" "$(cmp -s "$D/before.bin" "$D/download-before.bin" && echo yes || echo no)" yes
  head -c 4096 /dev/urandom > "$D/after.bin"
  curl -s -o capture-after.json -X POST -H "Authorization: Bearer $ACCESS" \
    -F "file=@after.bin;type=application/octet-stream" -F "description=after" \
    "$API/workspaces/$WS/projects/$PROJECT/evidence"
  EV2=$(jq -r .evidenceId "$D/capture-after.json")
  expect "new capture, download" "$(download "$EV2" download-after.bin)" 200
  expect "  byte-identical" "$(cmp -s "$D/after.bin" "$D/download-after.bin" && echo yes || echo no)" yes
  mcp_call "$TOKEN" > "$D/mcp-after.json"
  expect "machine token from v$PREVIOUS over MCP stdio, answer identical" "$(same_json "$D/mcp-before.json" "$D/mcp-after.json" .result)" yes

  docker restart "$P-api-1" "$P-mcp-1" "$P-evidence-1" > /dev/null
  wait_healthy "$P-evidence-1"; wait_healthy "$P-api-1"
  ACCESS=$(sign_in "$API" "$EMAIL" "$PASSWORD")
  expect "after a restart, before.bin" "$(download "$EV" /dev/null)" 200
  expect "after a restart, after.bin" "$(download "$EV2" /dev/null)" 200
  expect "error lines from api since the upgrade" "$(docker logs --since "$SINCE" "$P-api-1" 2>&1 | grep -cE '\[ERR\]|\bfail:|Unhandled')" 0
  cli scope-report > "$D/scope-report.out"
  expect "scope-report after the upgrade" "$?" 0
}

########## steps added by a release; a later release drops the ones about its predecessor ##########

# Whether an access token from the previous release survives. 401 when the release changes what a
# session is (v1.5.0), 200 otherwise.
SESSION_SURVIVES=200
access_token_from_before() {
  expect "access token issued on v$PREVIOUS, GET /me" "$(code -H "Authorization: Bearer $ACCESS_OLD" "$API/me")" "$SESSION_SURVIVES"
}

# Phase 14, A3: a plugin session open across the upgrade keeps the old image, stale-sessions.sh
# lists it, and closing the session removes it. The session is held open by a file descriptor on a
# FIFO, so closing that descriptor is the session's input closing.
open_session_across_upgrade() {
  mkfifo "$OUT/session.in"
  "${C[@]}" run --rm -T --no-deps mcp --stdio < "$OUT/session.in" > /dev/null 2>>"$ERR" &
  SESSION_PID=$!
  exec 7> "$OUT/session.in"
  sleep 10
  expect "a stdio session on v$PREVIOUS, open" "$(sh "$RELEASE_TOOLS/stale-sessions.sh" "$P" | tail -1)" \
    "sessions 1, on an older image than the mcp service 0"
}
session_left_on_the_old_image() {
  sh "$RELEASE_TOOLS/stale-sessions.sh" "$P" > "$D/stale-sessions.out"
  cat "$D/stale-sessions.out" | tee -a "$RES"
  expect "the session open across the upgrade is listed as stale" "$(tail -1 "$D/stale-sessions.out")" \
    "sessions 1, on an older image than the mcp service 1"
  exec 7>&-
  wait "$SESSION_PID"
  expect "the session exits when its input closes" "$?" 0
  expect "and its container is gone" "$(sh "$RELEASE_TOOLS/stale-sessions.sh" "$P" | tail -1)" \
    "sessions 0, on an older image than the mcp service 0"
}

BEFORE_UPGRADE=(previous_release seed open_session_across_upgrade)
DURING_UPGRADE=(upgrade)
AFTER_UPGRADE=(session_left_on_the_old_image access_token_from_before after_upgrade)

for step in "${BEFORE_UPGRADE[@]}" "${DURING_UPGRADE[@]}" "${AFTER_UPGRADE[@]}"; do
  "$step"
done
note "the stack is left running for inspection; remove it with: docker compose -p $P down -v"
finish
