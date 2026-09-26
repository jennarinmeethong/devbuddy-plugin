#!/usr/bin/env bash
# Release checklist, part 2, before the tag: Compose from clean, the destroy-and-restore drill, and
# tokens staying out of the log, against the clone setup-and-suite.sh made. Compose project
# devbuddy-v<version> on 127.0.0.1:18080/18081. No installation's own stack is touched, and the
# data is synthetic. The drill account's password is generated here and never printed.
#
#   stack-drill-tokens.sh VERSION
#
# DEVBUDDY_RELEASE_WORK  as for setup-and-suite.sh.
#
# A release adds a check by writing a function and adding its name to CHECKS at the bottom. The
# checks run in order in one shell, so a later one may use what the drill left: WS, PROJECT, ADMIN,
# RECORD, HASH, EMAIL, PASSWORD.
set -uo pipefail
source "$(dirname "$0")/lib.sh"

VERSION=${1:?version, e.g. 1.7.0}
WORK=${DEVBUDDY_RELEASE_WORK:-$HOME/devbuddy-release}
BASE=$WORK/v$VERSION
R=$BASE/repo
OUT=$BASE/results
D=$OUT/drill
T=$OUT/tokens
[ -d "$R" ] || { echo "$R is missing; run setup-and-suite.sh first" >&2; exit 1; }
mkdir -p "$D" "$T"
chmod 700 "$D" "$T"
cd "$R"

P=devbuddy-v$(compact "$VERSION")
ports_override "$OUT/ports.yaml" 18080 18081
C=(docker compose -p "$P" -f docker/compose.yaml -f "$OUT/ports.yaml")
API=http://127.0.0.1:18080
MCPURL=http://127.0.0.1:18081
ERR=$OUT/part2-stderr.log
RES=$OUT/part2-results.txt
: > "$ERR"
: > "$RES"
MIGRATIONS=$(migration_count "$R")

########## the stack ##########

compose_from_clean() {
  note "=== compose from clean, built from source at $(git rev-parse --short HEAD)"
  expect "volumes for $P before" "$(docker volume ls -q | grep -c "^${P}_")" 0
  local start
  start=$(date +%s)
  "${C[@]}" up -d --build > "$OUT/up.log" 2>&1
  expect "up exit" "$?" 0
  wait_healthy "$P-api-1"
  expect "api health" "$(docker inspect -f '{{.State.Health.Status}}' "$P-api-1")" healthy
  note "api healthy $(( $(date +%s) - start ))s after the build started"
  "${C[@]}" ps -a --format '{{.Service}}	{{.State}}	{{.Status}}' | tee -a "$RES"
  expect "migrate exit" "$(docker inspect -f '{{.State.ExitCode}}' "$P-migrate-1")" 0
  expect "migrations in the history table" \
    "$(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc 'select count(*) from "__EFMigrationsHistory"')" "$MIGRATIONS"
  expect "last migration" \
    "$(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc 'select max("MigrationId") from "__EFMigrationsHistory"')" "$(last_migration "$R")"
  expect "record_embeddings absent on postgres:17-alpine" \
    "$(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc "select to_regclass('record_embeddings') is null")" t
  local _
  for _ in $(seq 1 30); do
    "${C[@]}" logs --no-log-prefix retention 2>&1 | grep -qiE 'deleted|pass' && break
    sleep 2
  done
  note "retention first pass: $("${C[@]}" logs --no-log-prefix retention 2>&1 | grep -iE 'deleted|pass|completed' | tail -2 | tr '\n' ' ')"
  expect "api /health" "$(code $API/health)" 200
  expect "api /operations unauthenticated" "$(code $API/operations)" 401
  expect "web UI /" "$(code $API/)" 200
  expect "mcp POST / unauthenticated" "$(code -X POST -H 'Content-Type: application/json' -d '{}' $MCPURL/)" 401
  expect "mcp GET /no-such-path (control)" "$(code $MCPURL/no-such-path)" 404
  expect "listening on 5432 or 9000" "$(ss -ltn | awk '{print $4}' | grep -cE ':(5432|9000)$')" 0
  note "published: $(docker ps --filter "name=$P-" --format '{{.Ports}}' | grep -oE '[0-9.]+:[0-9]+->[0-9]+' | tr '\n' ' ')"
}

services_not_root() {
  note "=== no service runs as root"
  local s
  for s in api mcp retention; do
    expect "$s uid" "$(uid_of "$P-$s-1")" 1654
  done
  expect "database uid" "$(uid_of "$P-database-1")" 70
  expect "evidence uid" "$(uid_of "$P-evidence-1")" 1000
  for s in api mcp retention evidence; do
    expect "$s read-only" "$(docker inspect -f '{{.HostConfig.ReadonlyRootfs}}' "$P-$s-1")" true
    expect "$s capabilities dropped" "$(docker inspect -f '{{.HostConfig.CapDrop}}' "$P-$s-1")" '\[ALL\]'
  done
  expect "evidence volume owner" "$(docker run --rm -v "${P}_evidence:/v:ro" alpine:3 stat -c '%u:%g' /v)" 1000:1000
  expect "log volume owner" "$(docker run --rm -v "${P}_logs:/v:ro" alpine:3 stat -c '%u:%g' /v)" 1654:1654
}

ai_operations() {
  note "=== the AI surface"
  cli operations --ai | ai_names > "$OUT/ai-operations.txt"
  expect "AI operations" "$(wc -l < "$OUT/ai-operations.txt" | tr -d ' ')" 20
}

########## the drill ##########

capture() {
  curl -s -o "$D/capture-$3.json" -w '%{http_code}' -X POST \
    -H "Authorization: Bearer $ACCESS" \
    -F "file=@$1;type=$2" -F "description=$3" \
    "$API/workspaces/$WS/projects/$PROJECT/evidence"
}

drill() {
  note "=== drill: before"
  EMAIL=drill-admin@devbuddy.test
  PASSWORD=$(openssl rand -hex 20)
  local boot
  boot=$(cli bootstrap --workspace-name "Release drill v$VERSION" --email "$EMAIL" --password "$PASSWORD" --project-name "Drill project")
  WS=$(awk '$1=="workspace"{print $2}' <<<"$boot")
  PROJECT=$(awk '$1=="project"{print $2}' <<<"$boot")
  ADMIN=$(awk '$1=="actor"{print $2}' <<<"$boot")
  note "bootstrap: workspace $WS project $PROJECT actor $ADMIN"
  [ -n "$WS" ] && [ -n "$PROJECT" ] && [ -n "$ADMIN" ] || { expect bootstrap failed ok; tail -20 "$ERR"; finish; exit 1; }

  local wi now ev1 ev2 h1 h2 token ref doomed missing
  wi=$(op create_work_item "$(jq -nc --argjson s "$(scope_json)" '{key:"DRILL-1", type:"Develop", title:"Release drill work", goal:"Survive losing the database and the evidence store", scope:$s}')" | jq -r .workItemId)
  note "work item $wi"
  ACCESS=$(sign_in "$API" "$EMAIL" "$PASSWORD")
  local access_old=$ACCESS
  expect "sign-in before" "$(present "$ACCESS")" yes

  {
    echo "Release drill evidence for v$VERSION. Synthetic content, written by the drill script."
    echo "It exists so that the Evidence row of the restore checklist has bytes to compare,"
    echo "not merely a row saying bytes exist. Nothing here is project data."
  } > "$D/ev-text.txt"
  head -c 64 /dev/urandom > "$D/ev-bin.bin"
  # Credential-shaped values, generated here, that capture must refuse with nothing written.
  {
    echo "ConnectionStrings__Billing=Server=prod-db.internal;Database=billing;User Id=sa;Password=$(openssl rand -hex 12)"
    echo "aws_access_key_id = AKIA$(openssl rand -hex 8 | tr a-f A-F)"
    echo "aws_secret_access_key = $(openssl rand -base64 30 | tr -d '/+=' | head -c 40)"
  } > "$D/ev-blocked.txt"

  expect "capture text" "$(capture "$D/ev-text.txt" text/plain drill-text)" 20[01]
  expect "capture binary" "$(capture "$D/ev-bin.bin" application/octet-stream drill-binary)" 20[01]
  expect "capture carrying secrets" "$(capture "$D/ev-blocked.txt" text/plain drill-blocked)" 4[0-9][0-9]
  note "  refusal: $(jq -c '{title, detail}' "$D/capture-drill-blocked.json" 2>/dev/null)"
  ev1=$(jq -r .evidenceId "$D/capture-drill-text.json")
  ev2=$(jq -r .evidenceId "$D/capture-drill-binary.json")
  h1=$(sha256sum "$D/ev-text.txt" | cut -d' ' -f1)
  h2=$(sha256sum "$D/ev-bin.bin" | cut -d' ' -f1)
  expect "evidence stored before" "$(op list_evidence "$(jq -nc --argjson s "$(scope_json)" '{scope:$s}')" | jq '.evidence | length')" 2

  now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  RECORD=$(op create_draft "$(jq -nc --arg w "$wi" --argjson s "$(scope_json)" --arg now "$now" --arg v "$VERSION" '{workItemId:$w, kind:"TechnicalKnowledge", title:"Release drill record", body:("The first draft of the v" + $v + " drill record. A reviewer sends it back."), frontMatter:{component:"release-drill"}, provenance:{sourceKind:"HumanAuthored", sourceLocator:"release-drill", author:"drill-admin", recordedAt:$now}, scope:$s}')" | jq -r .recordId)
  note "draft record $RECORD"
  cli run get_record --actor "$ADMIN" --arguments "$(record_args "$RECORD")" > /dev/null 2>&1
  expect "never-published record read with no revision number is refused" "$?" "[1-9][0-9]*"
  op submit_for_approval "$(record_args "$RECORD")" > /dev/null
  expect "send back" "$(op request_correction "$(jq -nc --arg r "$RECORD" --argjson s "$(scope_json)" '{recordId:$r, reason:"Cite the evidence the drill captured.", scope:$s}')" | jq -r .status)" Draft
  expect "revise" "$(op revise_draft "$(jq -nc --arg r "$RECORD" --argjson s "$(scope_json)" --arg now "$now" --arg e1 "$ev1" --arg e2 "$ev2" --arg v "$VERSION" '{recordId:$r, title:"Release drill record", body:("The drill record for v" + $v + ". It must come back published at revision 2, with its front matter, its evidence references, the reason it was sent back, and its approval still bound to the content hash recorded before the disaster."), frontMatter:{component:"release-drill", release:("v" + $v)}, provenance:{sourceKind:"HumanAuthored", sourceLocator:"release-drill", author:"drill-admin", recordedAt:$now, evidence:[{evidenceObjectId:$e1, description:"drill text"}, {evidenceObjectId:$e2, description:"drill binary"}]}, scope:$s}')" | jq -r .currentRevisionNumber)" 2

  HASH=$(op view_record_history "$(record_args "$RECORD")" | jq -r '.revisions[-1].contentHash')
  cli run approve_record --actor "$ADMIN" --arguments "$(jq -nc --arg r "$RECORD" --arg h "$HASH" --argjson s "$(scope_json)" '{recordId:$r, approvedContentHash:$h, scope:$s}')" > /dev/null 2>&1
  expect "approve before submit is refused" "$?" "[1-9][0-9]*"
  op submit_for_approval "$(record_args "$RECORD")" > /dev/null
  op approve_record "$(jq -nc --arg r "$RECORD" --arg h "$HASH" --argjson s "$(scope_json)" '{recordId:$r, approvedContentHash:$h, scope:$s}')" > /dev/null
  expect "publish" "$(op publish_record "$(record_args "$RECORD")" | jq -r .publishedRevisionNumber)" 2
  note "content hash before: $HASH"
  op enable_project_ai_access "$(jq -nc --argjson s "$(scope_json)" '{scope:$s}')" > /dev/null

  token=$(op issue_machine_token "$(jq -nc --arg ws "$WS" '{name:"drill-plugin", lifetimeDays:30, workspaceId:$ws}')" | jq -r .token)
  mcp_call "$token" > "$D/mcp-before.json"
  expect "mcp stdio with the machine token, before" "$(jq -r '.result.isError // false' "$D/mcp-before.json" 2>/dev/null)" false

  op get_record "$(record_args "$RECORD")" > "$D/record-before.json"
  op view_record_history "$(record_args "$RECORD")" > "$D/history-before.json"
  expect "evidence references before" "$(jq '.evidence | length' "$D/record-before.json")" 2
  expect "corrections before" "$(jq '.corrections | length' "$D/history-before.json")" 1
  op read_audit_history "$(audit_args)" > "$D/audit-before.json"
  note "audit entries before: $(jq '.entries | length' "$D/audit-before.json"); by channel: $(channels "$D/audit-before.json")"

  doomed=$(op create_project "$(jq -nc --arg ws "$WS" '{workspaceId:$ws, name:"Deleted after the backup"}')" | jq -r .projectId)
  ref=$(cli backup --workspace "$WS" --actor "$ADMIN" | last_json | jq -r .reference)
  note "backup reference $ref"
  op delete_project "$(jq -nc --arg ws "$WS" --arg p "$doomed" '{scope:{workspaceId:$ws, projectId:$p}}')" > /dev/null
  expect "deletion ledger lines" "$(docker run --rm -v "${P}_backups:/b:ro" alpine:3 sh -c 'wc -l < /b/deletions.jsonl' 2>&1)" 1
  docker run --rm -v "${P}_backups:/b:ro" -v "$D:/o" alpine:3 sh -c "cp -r /b/$ref /o/backup-copy && chown -R $(id -u):$(id -g) /o/backup-copy"
  note "backup copied off the volume: $(du -sb "$D/backup-copy" | cut -f1) bytes"

  note "=== drill: disaster"
  "${C[@]}" stop api mcp retention >> "$ERR" 2>&1
  "${C[@]}" down >> "$ERR" 2>&1
  docker volume rm "${P}_database" "${P}_evidence" >> "$ERR" 2>&1
  expect "database and evidence volumes left" "$(docker volume ls -q | grep -cE "^${P}_(database|evidence)$")" 0

  note "=== drill: restore"
  "${C[@]}" up -d database evidence >> "$ERR" 2>&1
  wait_healthy "$P-database-1" && wait_healthy "$P-evidence-1"
  cli > "$D/migrate-empty.out"
  expect "migrate into an empty database" "$?" 0
  cli restore --reference "$ref" > "$D/restore.out"
  expect "restore exit" "$?" 0
  "${C[@]}" up -d >> "$ERR" 2>&1
  wait_healthy "$P-api-1"
  expect "api after restore" "$(docker inspect -f '{{.State.Health.Status}}' "$P-api-1")" healthy
  expect "evidence store after restore, uid" "$(uid_of "$P-evidence-1")" 1000
  expect "recreated evidence volume owner" "$(docker run --rm -v "${P}_evidence:/e:ro" alpine:3 stat -c '%u:%g' /e)" 1000:1000

  note "=== drill: after"
  expect "project deleted after the backup, rows after restore" \
    "$(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc "select count(*) from projects where id='$doomed'")" 0
  op get_record "$(record_args "$RECORD")" > "$D/record-after.json"
  op view_record_history "$(record_args "$RECORD")" > "$D/history-after.json"
  expect "record identical, front matter and evidence included" "$(same_json "$D/record-before.json" "$D/record-after.json")" yes
  expect "history identical, corrections included" "$(same_json "$D/history-before.json" "$D/history-after.json")" yes
  expect "content hash after" "$(jq -r '.revisions[-1].contentHash' "$D/history-after.json")" "$HASH"

  op read_audit_history "$(audit_args)" > "$D/audit-after.json"
  note "audit entries after: $(jq '.entries | length' "$D/audit-after.json"); by channel: $(channels "$D/audit-after.json")"
  missing=$(comm -23 <(jq -c '.entries[]' "$D/audit-before.json" | sort) <(jq -c '.entries[]' "$D/audit-after.json" | sort) | wc -l)
  expect "audit entries from before missing after, channel included" "$missing" 0

  ACCESS=$(sign_in "$API" "$EMAIL" "$PASSWORD")
  expect "sign-in after, same password" "$(present "$ACCESS")" yes
  expect "evidence stored after" "$(op list_evidence "$(jq -nc --argjson s "$(scope_json)" '{scope:$s}')" | jq '.evidence | length')" 2
  local pair id rest want label c
  for pair in "$ev1:$h1:text" "$ev2:$h2:bin"; do
    id=${pair%%:*}; rest=${pair#*:}; want=${rest%%:*}; label=${rest#*:}
    c=$(curl -s -o "$D/download-$label" -w '%{http_code}' -H "Authorization: Bearer $ACCESS" "$API/workspaces/$WS/projects/$PROJECT/evidence/$id")
    expect "download $label" "$c" 200
    expect "download $label byte-identical" "$(sha256sum "$D/download-$label" | cut -d' ' -f1)" "$want"
  done

  mcp_call "$token" > "$D/mcp-after.json"
  expect "mcp stdio, same machine token, answer identical" "$(same_json "$D/mcp-before.json" "$D/mcp-after.json" .result)" yes
  mcp_call "dbt_not-a-real-token" > "$D/mcp-bogus.json"
  note "mcp stdio, bogus token (control): $(head -c 300 "$D/mcp-bogus.json")"
  expect "bogus token answered like the real one" "$(same_json "$D/mcp-before.json" "$D/mcp-bogus.json" .result)" no

  cli restore --reference "$ref" > "$D/restore-again.out" 2>&1
  expect "second restore is refused" "$?" "[1-9][0-9]*"
  note "  $(grep -iE 'already|refus|empty' "$D/restore-again.out" | tail -1)"
  expect "access token issued before the disaster, GET /me" "$(code -H "Authorization: Bearer $access_old" "$API/me")" 401
}

########## tokens stay out of the log ##########

file_log() { docker run --rm -v "${P}_logs:/l:ro" alpine:3 sh -c 'cat /l/*.log'; }
request_recovery() {
  code -X POST "$API/auth/recovery/begin" -H 'Content-Type: application/json' -d "$(jq -nc --arg e "$EMAIL" '{email:$e}')"
}
grab() {
  sleep 5
  docker logs --since "$2" "$P-api-1" > "$T/$1-stdout.log" 2>&1
  file_log | tail -n +"$(($3 + 1))" > "$T/$1-file.log"
}
interesting() { grep -iE 'deliver|recover|token' "$@" | grep -vE 'SELECT|INSERT|UPDATE|FROM'; }

tokens_stay_out_of_the_log() {
  local lines since leaked v
  note "=== tokens: default"
  expect "AllowTokensInLog on the running api" \
    "$(docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$P-api-1" | grep AllowTokensInLog)" '.*=false'
  lines=$(file_log | wc -l); since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  expect "recovery begin, account exists" "$(request_recovery)" 202
  grab default "$since" "$lines"
  interesting "$T/default-file.log" | head -3 | tee -a "$RES"
  expect "could-not-be-delivered lines, standard output and file"     "$(cat "$T/default-stdout.log" "$T/default-file.log" | grep -c 'could not be delivered and its contents were not written')" "[1-9][0-9]*"

  note "=== tokens: opt-in"
  DEVBUDDY_EMAIL_ALLOW_TOKENS_IN_LOG=true "${C[@]}" up -d --no-deps api > "$T/recreate-optin.log" 2>&1
  wait_healthy "$P-api-1"
  lines=$(file_log | wc -l); since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  expect "recovery begin under the opt-in" "$(request_recovery)" 202
  grab optin "$since" "$lines"
  interesting "$T/optin-file.log" | sed -E 's/[A-Za-z0-9_-]{32,}/<LONG-VALUE>/g' | head -3 | tee -a "$RES"
  grep -ohE '[A-Za-z0-9_-]{32,}' "$T/optin-stdout.log" "$T/optin-file.log" | sort -u > "$T/optin-long-values.txt"
  expect "long values written under the opt-in" "$(wc -l < "$T/optin-long-values.txt" | tr -d ' ')" "[1-9][0-9]*"
  leaked=0
  while read -r v; do
    grep -qF "$v" "$T/default-stdout.log" "$T/default-file.log" && leaked=$((leaked + 1))
  done < "$T/optin-long-values.txt"
  expect "of those, found in the default capture" "$leaked" 0
  "${C[@]}" up -d --no-deps api > "$T/recreate-default.log" 2>&1
  wait_healthy "$P-api-1"
  expect "api back on the default" "$(docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$P-api-1" | grep AllowTokensInLog)" '.*=false'
}

########## checks added by a release, kept for every release after it ##########

# v1.4.0: the project in a scope is a claim, and scope-report reads what came before the check.
project_in_scope() {
  note "=== the project in a scope (v1.4.0)"
  local fake fake_scope
  fake=$(cat /proc/sys/kernel/random/uuid)
  fake_scope=$(jq -nc --arg ws "$WS" --arg p "$fake" '{workspaceId:$ws, projectId:$p}')
  cli run create_work_item --actor "$ADMIN" --arguments "$(jq -nc --argjson s "$fake_scope" '{key:"GHOST-1", type:"Develop", title:"Against a made-up project", goal:"Must be refused", scope:$s}')" > "$D/ghost.out" 2>&1
  expect "create_work_item against a made-up project is refused" "$?" "[1-9][0-9]*"
  expect "work items stored against it" \
    "$(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc "select count(*) from work_items where project_id='$fake'")" 0
  cli scope-report > "$D/scope-report.out"
  expect "scope-report exit" "$?" 0
  expect "list_source_repositories is human-only" "$(cli operations | awk '$1=="list_source_repositories"{print $NF}')" human-only
}

# v1.5.0: the IndexMaintainer role runs the stale-record sweep, and nothing wider.
index_maintainer() {
  note "=== IndexMaintainer and the stale-record sweep (v1.5.0)"
  IMID=$(op create_user_account "$(jq -nc --arg ws "$WS" '{workspaceId:$ws, email:"index-maintainer@devbuddy.test", displayName:"Index maintainer", role:"IndexMaintainer"}')" | jq -r .userId)
  local imtoken vwtoken
  imtoken=$(cli run issue_machine_token --actor "$IMID" --arguments "$(jq -nc --arg ws "$WS" '{name:"stale-sweep", lifetimeDays:30, workspaceId:$ws}')" | last_json | jq -r .token)
  expect "IndexMaintainer machine token issued" "$(present "$imtoken")" yes
  "${C[@]}" run --rm -T --no-deps -e DEVBUDDY_WORKER_TOKEN="$imtoken" migrate worker stale-record-sweep --stale-after 1d > "$D/stale-im.out" 2>>"$ERR"
  expect "stale-record-sweep as IndexMaintainer" "$?" 0
  VW=$(op create_user_account "$(jq -nc --arg ws "$WS" '{workspaceId:$ws, email:"viewer@devbuddy.test", displayName:"Viewer", role:"Viewer"}')" | jq -r .userId)
  vwtoken=$(cli run issue_machine_token --actor "$VW" --arguments "$(jq -nc --arg ws "$WS" '{name:"viewer-sweep", lifetimeDays:30, workspaceId:$ws}')" | last_json | jq -r .token)
  "${C[@]}" run --rm -T --no-deps -e DEVBUDDY_WORKER_TOKEN="$vwtoken" migrate worker stale-record-sweep --stale-after 1d > "$D/stale-viewer.out" 2>>"$ERR"
  expect "stale-record-sweep as a Viewer is refused" "$(grep -c 'does not carry ManageIndex' "$D/stale-viewer.out")" "[1-9][0-9]*"
  cli run create_project --actor "$IMID" --arguments "$(jq -nc --arg ws "$WS" '{workspaceId:$ws, name:"Not for an index maintainer"}')" > "$D/im-create.out" 2>&1
  expect "IndexMaintainer creating a project is refused" "$?" "[1-9][0-9]*"
}

# v1.5.0: an administrator issues a reset. Needs VW from index_maintainer.
password_reset() {
  note "=== administrator-issued password reset (v1.5.0)"
  local reset newpw
  reset=$(op issue_password_reset "$(jq -nc --arg ws "$WS" --arg u "$VW" '{workspaceId:$ws, subjectUserId:$u}')" | jq -r .resetToken)
  newpw=$(openssl rand -hex 20)
  expect "issue_password_reset" "$(present "$reset")" yes
  expect "recovery/complete" "$(code -X POST "$API/auth/recovery/complete" -H 'Content-Type: application/json' -d "$(jq -nc --arg t "$reset" --arg p "$newpw" '{token:$t, newPassword:$p}')")" "20[04]"
  expect "the viewer signs in with the new password" "$(present "$(sign_in "$API" viewer@devbuddy.test "$newpw")")" yes
}

# v1.5.0: marking a record as AI-written leaves its content hash, and so its approval, alone.
ai_marking() {
  note "=== mark_record_ai_generated (v1.5.0)"
  op mark_record_ai_generated "$(jq -nc --arg r "$RECORD" --argjson s "$(scope_json)" '{recordId:$r, reason:"The release drill says so, to check the operation.", scope:$s}')" > "$D/mark.json"
  expect "content hash after marking" "$(op view_record_history "$(record_args "$RECORD")" | jq -r '.revisions[-1].contentHash')" "$HASH"
}

# v1.5.0: embedding-check and scope-report --delete.
embedding_check_and_scope_delete() {
  note "=== embedding-check and scope-report --delete (v1.5.0)"
  cli embedding-check --budget 0 > "$D/embedding-check.out"
  expect "embedding-check with no provider" "$?" 0
  # With nothing stray, --delete stops before it compares the count, so this checks it deletes nothing.
  cli scope-report --delete --confirm 5 --actor "$ADMIN" > "$D/scope-delete.out" 2>&1
  expect "scope-report --delete on a clean installation" "$?" 0
  expect "  deletes nothing" "$(grep -c 'Nothing was deleted' "$D/scope-delete.out")" 1
}

# v1.6.0: a self-hosted endpoint must be on a private address, and the dialect setting is gone.
selfhosted_is_private() {
  note "=== SelfHosted must be private (v1.6.0)"
  emb() { "${C[@]}" run --rm -T --no-deps -e DEVBUDDY_Embedding__Provider=SelfHosted -e DEVBUDDY_Embedding__Endpoint="$1" -e DEVBUDDY_Embedding__Model=qwen3-embedding:0.6b -e DEVBUDDY_Embedding__Dimensions=1024 migrate embedding-check --budget 0; }
  emb http://203.0.113.10:11434/v1 > "$D/emb-public.out" 2> "$D/emb-public.err"
  expect "SelfHosted on a public literal is refused at start-up" "$?" 2
  expect "  and says why" "$(grep -c 'is on the public address 203.0.113.10' "$D/emb-public.err")" "[1-9][0-9]*"
  emb http://ollama:11434/v1 > "$D/emb-name.out" 2> "$D/emb-name.err"
  expect "SelfHosted on a private name starts" "$(grep -cE '^ok +provider +SelfHosted' "$D/emb-name.out")" 1
  emb http://192.168.1.20:11434/v1 > "$D/emb-lan.out" 2> "$D/emb-lan.err"
  expect "SelfHosted on a LAN literal starts" "$(grep -cE '^ok +provider +SelfHosted' "$D/emb-lan.out")" 1
  expect "a dialect anywhere in the output" "$(cat "$D"/emb-*.out | grep -ci dialect)" 0
  "${C[@]}" run --rm -T --no-deps -e DEVBUDDY_Embedding__Dialect=VoyageAi migrate embedding-check --budget 0 > "$D/emb-dialect.out" 2> "$D/emb-dialect.err"
  expect "a leftover Embedding:Dialect setting is ignored" "$?" 0
}

# v1.7.0 (Phase 14, C1): the query instruction is optional, one line, and at most 500 characters.
query_instruction() {
  note "=== the query instruction (v1.7.0)"
  qi() { "${C[@]}" run --rm -T --no-deps -e DEVBUDDY_Embedding__Provider=SelfHosted -e DEVBUDDY_Embedding__Endpoint=http://ollama:11434/v1 -e DEVBUDDY_Embedding__Model=qwen3-embedding:0.6b -e DEVBUDDY_Embedding__Dimensions=1024 -e DEVBUDDY_Embedding__QueryInstruction="$1" migrate embedding-check --budget 0; }
  qi 'Given a web search query, retrieve relevant passages that answer the query' > "$D/qi-published.out" 2> "$D/qi-published.err"
  expect "Qwen3's published instruction starts" "$(grep -cE '^ok +provider +SelfHosted' "$D/qi-published.out")" 1
  qi "$(printf 'one line\nQuery: a second')" > "$D/qi-lines.out" 2> "$D/qi-lines.err"
  expect "an instruction with a line break is refused at start-up" "$?" 2
  expect "  and says why" "$(grep -c 'QueryInstruction must be a single line' "$D/qi-lines.err")" "[1-9][0-9]*"
  qi "$(printf 'x%.0s' $(seq 501))" > "$D/qi-long.out" 2> "$D/qi-long.err"
  expect "an instruction of 501 characters is refused at start-up" "$?" 2
  expect "  and says why" "$(grep -c 'QueryInstruction must be at most 500 characters' "$D/qi-long.err")" "[1-9][0-9]*"
}

# v1.7.0: the evidence store is MinIO built from its source, pinned by commit, in an image holding
# nothing else (info.md, 2026-09-25).
evidence_built_from_source() {
  note "=== the evidence store is built from source (v1.7.0)"
  local image
  image=$(docker inspect -f '{{.Config.Image}}' "$P-evidence-1")
  note "evidence image: $image $(docker image inspect -f '{{.Id}}' "$image" | cut -c1-19)"
  expect "  minio reports the pinned release" "$(docker run --rm "$image" --version 2>&1 | grep -c 'RELEASE.2025-04-22T22-12-26Z')" "[1-9][0-9]*"
  expect "  mc reports the pinned release" "$(docker run --rm --entrypoint mc "$image" --version 2>&1 | grep -c 'RELEASE.2025-04-16T18-13-26Z')" "[1-9][0-9]*"
  docker run --rm --entrypoint sh "$image" -c true > /dev/null 2>&1
  expect "  there is no shell to run" "$([ $? -ne 0 ] && echo refused || echo ran)" refused
  expect "  it holds two binaries and nothing else in /usr/bin" "$(docker create "$image" > "$D/evidence-cid" && docker export "$(cat "$D/evidence-cid")" | tar -t | grep -E '^usr/bin/.' | sort | tr '\n' ' '; docker rm "$(cat "$D/evidence-cid")" > /dev/null)" "usr/bin/mc usr/bin/minio "
}

# v1.8.0 (Phase 14, C4): a NUL in text is refused before PostgreSQL sees it, and every answer
# carries the security headers, with COOP only when the page arrived over HTTPS.
nul_and_headers() {
  note "=== NUL refused, and the security headers (v1.8.0)"
  local h
  expect "a NUL in the recovery address is refused" \
    "$(code -X POST "$API/auth/recovery/begin" -H 'Content-Type: application/json' -d '{"email":"a\u0000b@example.com"}')" 400
  h=$(curl -s -D - -o /dev/null "$API/")
  expect "  the client carries a CSP forbidding framing" "$(grep -ci "^content-security-policy:.*frame-ancestors 'none'" <<< "$h")" 1
  expect "  and nosniff" "$(grep -ci '^x-content-type-options: nosniff' <<< "$h")" 1
  expect "  and no COOP over plain HTTP" "$(grep -ci '^cross-origin-opener-policy:' <<< "$h")" 0
  expect "  COOP when a proxy says HTTPS" \
    "$(curl -s -D - -o /dev/null -H 'X-Forwarded-Proto: https' "$API/" | grep -ci '^cross-origin-opener-policy: same-origin')" 1
}

# v1.8.0: a mail server that fails is logged and never thrown, so account recovery answers the same
# for an address with an account and one without.
email_failure_not_thrown() {
  note "=== a failing mail server (v1.8.0)"
  local since nobody
  nobody="nobody-$(cat /proc/sys/kernel/random/uuid)@example.com"
  DEVBUDDY_EMAIL_PROVIDER=Smtp DEVBUDDY_SMTP_HOST=127.0.0.1 DEVBUDDY_SMTP_PORT=1 \
    "${C[@]}" up -d --no-deps api > "$T/recreate-smtp.log" 2>&1
  wait_healthy "$P-api-1"
  since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  expect "recovery for the account, mail server refusing" "$(request_recovery)" 202
  expect "recovery for an address with no account" \
    "$(code -X POST "$API/auth/recovery/begin" -H 'Content-Type: application/json' -d "$(jq -nc --arg e "$nobody" '{email:$e}')")" 202
  sleep 3
  docker logs --since "$since" "$P-api-1" > "$T/smtp-stdout.log" 2>&1
  expect "  the failure is logged" "$(grep -c 'could not be delivered through SMTP server' "$T/smtp-stdout.log")" "[1-9][0-9]*"
  expect "  and nothing was unhandled" "$(grep -ciE 'unhandled exception' "$T/smtp-stdout.log")" 0
  "${C[@]}" up -d --no-deps api > "$T/recreate-default-2.log" 2>&1
  wait_healthy "$P-api-1"
}

# v1.8.0: the host ports start at 5010 (info.md, 2026-09-26), still on loopback.
default_ports() {
  note "=== the default host ports (v1.8.0)"
  expect "published by default" \
    "$(docker compose -p "$P" -f docker/compose.yaml config --format json | jq -r '[.services[] | .ports[]? | "\(.host_ip):\(.published)->\(.target)"] | sort | join(" ")')" \
    "127.0.0.1:5010->8080 127.0.0.1:5011->8080"
}

# The order matters: drill makes the workspace every later check works in.
CHECKS=(
  compose_from_clean
  services_not_root
  ai_operations
  drill
  tokens_stay_out_of_the_log
  project_in_scope
  index_maintainer
  password_reset
  ai_marking
  embedding_check_and_scope_delete
  selfhosted_is_private
  query_instruction
  evidence_built_from_source
  nul_and_headers
  email_failure_not_thrown
  default_ports
)

for check in "${CHECKS[@]}"; do
  "$check"
done
note "retention log, last line: $("${C[@]}" logs --no-log-prefix retention 2>&1 | grep -iE 'completed|pass|deleted' | tail -1)"
note "the stack is left running for inspection; remove it with: docker compose -p $P down -v"
finish
