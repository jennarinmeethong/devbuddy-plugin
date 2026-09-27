#!/bin/sh
# Checks a hosted model server from outside, the way DevBuddy's hosted mode will reach it (Phase 14
# B3). Sends one synthetic sentence and nothing else. The key comes from the environment and is
# never printed.
#
#   DEVBUDDY_EMBEDDING_API_KEY=... sh check.sh https://embed.example.com [CA_FILE] [MODEL] [DIMENSIONS]
#
# CA_FILE is for a test server on `tls internal`; a real one needs none. EMBED_HTTP_URL overrides
# the plain-HTTP address when port 80 is mapped elsewhere. Exits 1 if a check failed.
set -u
URL=${1:?base URL, e.g. https://embed.example.com}
CA=${2:-}
MODEL=${3:-qwen3-embedding:0.6b}
DIMENSIONS=${4:-1024}
KEY=${DEVBUDDY_EMBEDDING_API_KEY:?the bearer key}

FAILED=0
check() { # label actual wanted
  if [ "$2" = "$3" ]; then echo "ok   $1: $2"; else echo "FAIL $1: $2 (wanted $3)"; FAILED=$((FAILED + 1)); fi
}
code() { # curl arguments...
  if [ -n "$CA" ]; then set -- --cacert "$CA" "$@"; fi
  curl -s -o /dev/null -w '%{http_code}' --max-time 60 "$@"
}
BODY="{\"model\":\"$MODEL\",\"input\":[\"A synthetic sentence about a release checklist.\"]}"

# Keys go to curl as header files, so neither appears in the process list.
HEADERS=$(mktemp)
WRONG=$(mktemp)
trap 'rm -f "$HEADERS" "$HEADERS.out" "$WRONG"' EXIT
header() { printf 'Authorization: Bearer %s\nContent-Type: application/json\n' "$1"; }
header "$KEY" > "$HEADERS"
header not-the-key > "$WRONG"

check "no key" "$(code -X POST -H 'Content-Type: application/json' -d "$BODY" "$URL/v1/embeddings")" 401
check "wrong key" "$(code -X POST -H "@$WRONG" -d "$BODY" "$URL/v1/embeddings")" 401
check "Ollama's own API, with the key" "$(code -H "@$HEADERS" "$URL/api/tags")" 404
check "a model pull, with the key" "$(code -X POST -H "@$HEADERS" -d '{"name":"x"}' "$URL/api/pull")" 404
check "GET /v1/embeddings, with the key" "$(code -H "@$HEADERS" "$URL/v1/embeddings")" 404

if [ -n "$CA" ]; then CAARG="--cacert $CA"; else CAARG=""; fi
# shellcheck disable=SC2086 # CAARG is empty or two words on purpose
STATUS=$(curl -s $CAARG -o "$HEADERS.out" -w '%{http_code}' --max-time 120 -X POST -H "@$HEADERS" -d "$BODY" "$URL/v1/embeddings")
check "embedding, with the key" "$STATUS" 200
# One vector of DIMENSIONS numbers: count the commas in the first embedding array.
LENGTH=$(sed -n 's/.*"embedding":\[\([^]]*\)\].*/\1/p' "$HEADERS.out" | tr ',' '\n' | grep -c .)
check "dimensions" "$LENGTH" "$DIMENSIONS"

HOST=$(echo "$URL" | sed -E 's#^https://([^/:]+).*#\1#')
HTTP_URL=${EMBED_HTTP_URL:-http://$HOST}
check "plain HTTP is only a redirect" "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$HTTP_URL/v1/embeddings")" 308
echo "=== $FAILED failed"
[ "$FAILED" -eq 0 ]
