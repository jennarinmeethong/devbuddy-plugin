#!/usr/bin/env bash
# Phase 14, C2: measures whether semantic search finds the right record.
#
# Loads set.json into a throwaway stack built from this checkout, with pgvector, through the
# product itself: each record is drafted, approved and published over the HTTP API, the real
# record-embedding-sweep embeds them as a Viewer account of its own, and every question is asked
# through search_similar_records over the MCP server's stdio transport, on the AI channel, as an
# assistant would ask it. The numbers therefore include chunking, redaction and ranking, not only
# the model. Reports recall@1, recall@5 and MRR (over the top ten), overall and per language pair.
#
#   evaluate.sh WORKDIR
#
# The model server, one of:
#   EVAL_OLLAMA_VOLUME  a Docker volume holding Ollama's models, mounted READ-ONLY into an Ollama
#                       started here from the image the devbox pins; the installation owning the
#                       volume is not touched. Endpoint http://ollama:11434/v1.
#   EVAL_ENDPOINT       any OpenAI-compatible server the containers can reach on a private address,
#                       such as LM Studio on the LAN.
# EVAL_MODEL (default qwen3-embedding:0.6b), EVAL_DIMENSIONS (default 1024), EVAL_CHUNK (default
# 3000), EVAL_QUERY_INSTRUCTION (default none; Phase 14, C1), EVAL_SET (default set.json beside
# this script).
#
# Synthetic data only. Secrets are generated here and never printed. The stack is removed at the
# end unless EVAL_KEEP=1. WORKDIR must not exist.
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
source "$HERE/../release/lib.sh"

WORK=${1:?work dir}
SET=${EVAL_SET:-$HERE/set.json}
MODEL=${EVAL_MODEL:-qwen3-embedding:0.6b}
DIMENSIONS=${EVAL_DIMENSIONS:-1024}
CHUNK=${EVAL_CHUNK:-3000}
INSTRUCTION=${EVAL_QUERY_INSTRUCTION:-}
OLLAMA_IMAGE=ollama/ollama@sha256:684d8674b4315fa18f4f0e973a118ec2652ed96f67563277839985175858e0ba
R=$(git -C "$HERE" rev-parse --show-toplevel)
P=devbuddy-retrieval

if [ -n "${EVAL_OLLAMA_VOLUME:-}" ]; then
  ENDPOINT=http://ollama:11434/v1
elif [ -n "${EVAL_ENDPOINT:-}" ]; then
  ENDPOINT=$EVAL_ENDPOINT
else
  echo "set EVAL_OLLAMA_VOLUME or EVAL_ENDPOINT" >&2
  exit 2
fi

fresh_dir "$WORK"
RES=$WORK/results.txt
ERR=$WORK/stderr.log
: > "$RES"
: > "$ERR"

make_env "$WORK/.env" pgvector/pgvector:pg17
cat >> "$WORK/.env" <<EOF
DEVBUDDY_EMBEDDING_PROVIDER=SelfHosted
DEVBUDDY_EMBEDDING_ENDPOINT=$ENDPOINT
DEVBUDDY_EMBEDDING_MODEL=$MODEL
DEVBUDDY_EMBEDDING_DIMENSIONS=$DIMENSIONS
DEVBUDDY_EMBEDDING_BATCH_SIZE=8
DEVBUDDY_EMBEDDING_CHUNK_CHARACTERS=$CHUNK
DEVBUDDY_EMBEDDING_QUERY_INSTRUCTION=$INSTRUCTION
EOF

{
  echo "services:"
  echo "  api:"
  echo "    ports: !override"
  echo "      - \"127.0.0.1:48080:8080\""
  echo "  mcp:"
  echo "    ports: !reset []"
  if [ -n "${EVAL_OLLAMA_VOLUME:-}" ]; then
    cat <<EOF
  ollama:
    image: $OLLAMA_IMAGE
    user: "1000:1000"
    read_only: true
    security_opt: ["no-new-privileges:true"]
    cap_drop: [ALL]
    tmpfs: [/tmp]
    environment:
      HOME: /tmp
      OLLAMA_MODELS: /srv/ollama/models
      OLLAMA_HOST: 0.0.0.0:11434
      OLLAMA_NOPRUNE: "1"
      OLLAMA_KEEP_ALIVE: 1h
    volumes:
      - models:/srv/ollama:ro
    healthcheck:
      test: ["CMD", "/bin/ollama", "list"]
      interval: 5s
      retries: 20
    networks: [internal]
volumes:
  models:
    external: true
    name: $EVAL_OLLAMA_VOLUME
EOF
  fi
} > "$WORK/override.yaml"

C=(docker compose -p "$P" --env-file "$WORK/.env" -f "$R/docker/compose.yaml" -f "$WORK/override.yaml")
API=http://127.0.0.1:48080
cleanup() {
  [ "${EVAL_KEEP:-0}" = 1 ] || "${C[@]}" --profile workers down -v >> "$ERR" 2>&1
}
trap cleanup EXIT

note "=== retrieval evaluation: $MODEL at $ENDPOINT, chunk $CHUNK, commit $(git -C "$R" rev-parse --short HEAD)"
note "query instruction: ${INSTRUCTION:-none}"
note "set $(sha256sum "$SET" | cut -c1-12): $(jq '.records | length' "$SET") records, $(jq '.queries | length' "$SET") questions"
services=(api)
[ -n "${EVAL_OLLAMA_VOLUME:-}" ] && services+=(ollama)
# Every image first: a `compose run` that has to build prints the build log on standard output.
"${C[@]}" --profile workers build > "$WORK/build.log" 2>&1
"${C[@]}" up -d --wait "${services[@]}" > "$WORK/up.log" 2>&1
expect "stack up" "$?" 0

boot=$("${C[@]}" run --rm -T migrate bootstrap --workspace-name "Retrieval evaluation" \
  --email eval-admin@devbuddy.test --password "$(openssl rand -hex 20 | tee "$WORK/.admin")" \
  --project-name "Evaluation" 2>>"$ERR")
WS=$(awk '$1=="workspace"{print $2}' <<<"$boot")
PROJECT=$(awk '$1=="project"{print $2}' <<<"$boot")
ADMIN=$(awk '$1=="actor"{print $2}' <<<"$boot")
[ -n "$WS" ] && [ -n "$PROJECT" ] || { note "bootstrap failed"; exit 1; }
chmod 600 "$WORK/.admin"
ACCESS=$(sign_in "$API" eval-admin@devbuddy.test "$(cat "$WORK/.admin")")
expect "admin signed in" "$(present "$ACCESS")" yes

# invoke NAME JSON: one operation over the HTTP API, as the administrator.
invoke() {
  curl -s -X POST "$API/operations/$1" -H "Authorization: Bearer $ACCESS" \
    -H 'Content-Type: application/json' -d "$2"
}

invoke enable_project_ai_access "$(jq -nc --argjson s "$(scope_json)" '{scope:$s}')" > /dev/null
WI=$(invoke create_work_item "$(jq -nc --argjson s "$(scope_json)" '{key:"EVAL-1", type:"Develop", title:"Retrieval evaluation", goal:"Hold the evaluation records", scope:$s}')" | jq -r .workItemId)

note "=== publishing the records"
: > "$WORK/records.tsv"
now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
count=$(jq '.records | length' "$SET")
for i in $(seq 0 $((count - 1))); do
  rec=$(jq -c ".records[$i]" "$SET")
  key=$(jq -r .key <<<"$rec")
  id=$(invoke create_draft "$(jq -nc --argjson r "$rec" --arg w "$WI" --argjson s "$(scope_json)" --arg now "$now" \
    '{workItemId:$w, kind:$r.kind, title:$r.title, body:$r.body, frontMatter:{}, provenance:{sourceKind:"HumanAuthored", sourceLocator:("retrieval-set/" + $r.key), author:"retrieval-evaluation", recordedAt:$now}, scope:$s}')" | jq -r .recordId)
  args=$(jq -nc --arg r "$id" --argjson s "$(scope_json)" '{recordId:$r, scope:$s}')
  hash=$(invoke view_record_history "$args" | jq -r '.revisions[-1].contentHash')
  invoke submit_for_approval "$args" > /dev/null
  invoke approve_record "$(jq -nc --arg r "$id" --arg h "$hash" --argjson s "$(scope_json)" '{recordId:$r, approvedContentHash:$h, scope:$s}')" > /dev/null
  status=$(invoke publish_record "$args" | jq -r .status)
  [ "$status" = Published ] || note "FAIL $key was not published: $status"
  printf '%s\t%s\n' "$key" "$id" >> "$WORK/records.tsv"
done
expect "records published" "$(wc -l < "$WORK/records.tsv" | tr -d ' ')" "$count"

note "=== embedding them with the real sweep, as a Viewer"
VIEWER=$(invoke create_user_account "$(jq -nc --arg ws "$WS" '{workspaceId:$ws, email:"eval-viewer@devbuddy.test", displayName:"Evaluation viewer", role:"Viewer"}')" | jq -r .userId)
TOKEN=$("${C[@]}" run --rm -T migrate run issue_machine_token --actor "$VIEWER" \
  --arguments "$(jq -nc --arg ws "$WS" '{name:"retrieval evaluation", lifetimeDays:1, workspaceId:$ws}')" 2>>"$ERR" | last_json | jq -r .token)
expect "viewer token issued" "$(present "$TOKEN")" yes
export DEVBUDDY_WORKER_TOKEN=$TOKEN DEVBUDDY_TOKEN=$TOKEN
"${C[@]}" run --rm -T --no-deps -e DEVBUDDY_WORKER_TOKEN record-embedding-sweep \
  worker record-embedding-sweep --budget 1000 > "$WORK/sweep.out" 2>>"$ERR"
expect "sweep exit" "$?" 0
note "sweep: $(tr '\n' ' ' < "$WORK/sweep.out" | head -c 400)"
note "rows in the index: $(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc 'select count(*) from record_embeddings'), records: $(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc 'select count(distinct record_id) from record_embeddings')"

note "=== asking every question over MCP stdio"
queries=$(jq '.queries | length' "$SET")
jq -c --argjson s "$(scope_json)" '.queries | to_entries[] | {jsonrpc:"2.0", id:(.key + 100), method:"tools/call", params:{name:"search_similar_records", arguments:{scope:$s, queryText:.value.text, maxResults:10}}}' "$SET" > "$WORK/calls.jsonl"
mkfifo "$WORK/mcp.in"
"${C[@]}" run --rm -T --no-deps -e DEVBUDDY_TOKEN mcp --stdio < "$WORK/mcp.in" > "$WORK/mcp.out" 2>>"$ERR" &
MCP_PID=$!
exec 7> "$WORK/mcp.in"
jq -nc '{jsonrpc:"2.0", id:1, method:"initialize", params:{protocolVersion:"2025-06-18", capabilities:{}, clientInfo:{name:"retrieval-evaluation", version:"0"}}}' >&7
for _ in $(seq 1 60); do grep -q '"id":1' "$WORK/mcp.out" 2>/dev/null && break; sleep 1; done
jq -nc '{jsonrpc:"2.0", method:"notifications/initialized"}' >&7
cat "$WORK/calls.jsonl" >&7
for _ in $(seq 1 600); do
  [ "$(grep -c '"id":1[0-9][0-9]' "$WORK/mcp.out")" -ge "$queries" ] && break
  sleep 1
done
exec 7>&-
wait "$MCP_PID"
expect "answers received" "$(grep -c '"id":1[0-9][0-9]' "$WORK/mcp.out")" "$queries"

# One line per question: its language, the language of the record it expects, and the 1-based rank
# of that record in the answer, or 0 when it is not in the top ten.
jq -Rn --slurpfile set "$SET" --rawfile ids "$WORK/records.tsv" '
  ($ids | split("\n") | map(select(length > 0) | split("\t") | {(.[0]): .[1]}) | add) as $idOf
  | [inputs | fromjson? | select(type == "object" and .id != null and .id >= 100)] as $answers
  | $answers | map(
      (.id - 100) as $i
      | $set[0].queries[$i] as $q
      | (.result.structuredContent // (.result.content[0].text | fromjson)) as $r
      | ($r.hits | map(.recordId)) as $hits
      | {index: $i, question: $q.text, lang: $q.lang, recordLang: ($q.expects | split("-")[0]), expects: $q.expects,
         rank: (($hits | index($idOf[$q.expects])) as $p | if $p == null then 0 else $p + 1 end),
         unavailable: $r.unavailable, error: .result.isError}
    ) | sort_by(.index)' "$WORK/mcp.out" > "$WORK/ranks.json"
expect "errors or unavailable answers" "$(jq '[.[] | select(.error == true or .unavailable != null)] | length' "$WORK/ranks.json")" 0

metrics() {
  jq -r --arg label "$1" "$2"' | {n: length,
      r1: (map(select(.rank == 1)) | length),
      r5: (map(select(.rank >= 1 and .rank <= 5)) | length),
      mrr: (map(if .rank > 0 then 1 / .rank else 0 end) | add)}
    | "\($label)\tn=\(.n)\trecall@1=\(.r1 / .n * 1000 | round / 1000)\trecall@5=\(.r5 / .n * 1000 | round / 1000)\tMRR=\(.mrr / .n * 1000 | round / 1000)"' "$WORK/ranks.json" | tee -a "$RES"
}
note "=== results"
metrics "all" '.'
metrics "en question, en record" 'map(select(.lang == "en" and .recordLang == "en"))'
metrics "th question, th record" 'map(select(.lang == "th" and .recordLang == "th"))'
metrics "en question, th record" 'map(select(.lang == "en" and .recordLang == "th"))'
metrics "th question, en record" 'map(select(.lang == "th" and .recordLang == "en"))'
metrics "long records" 'map(select(.expects | test("long")))'
note "misses outside the top five:"
jq -r '.[] | select(.rank == 0 or .rank > 5) | "  rank \(.rank)  \(.expects)  \(.question)"' "$WORK/ranks.json" | tee -a "$RES"
finish
