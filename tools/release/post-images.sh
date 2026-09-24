#!/usr/bin/env bash
# Release checklist, after the tag: start the three PUBLISHED images with the tag's own Compose
# file, in a throwaway Compose project on 127.0.0.1:38080/38081, on whatever architecture this
# machine is. No data is created. Secrets are generated here and never printed. Portable to macOS
# bash 3.2 (no jq, no ss, no /proc), so it runs on the Mac mini as well as on Linux.
#
#   post-images.sh VERSION WORKDIR [DATABASE_IMAGE] [HELPER_IMAGE]
#
# WORKDIR must not exist. It keeps results/ai-operations.txt, the published console image's AI
# operation names in order, which smoke.sh and client-smoke.ps1 compare every archive against.
# On the Mac mini over SSH, Docker Hub is unreachable: pass
# public.ecr.aws/docker/library/postgres:17-alpine and public.ecr.aws/docker/library/alpine:3.
set -uo pipefail
source "$(dirname "$0")/lib.sh"

VERSION=${1:?version, e.g. 1.7.0}
WORKDIR=${2:?work dir}
DBIMAGE=${3:-postgres:17-alpine}
HELPER=${4:-alpine:3}
P=devbuddy-rel$(compact "$VERSION")
R=$WORKDIR/repo
OUT=$WORKDIR/results

fresh_dir "$WORKDIR"
mkdir -p "$OUT"
RES=$OUT/post-images-results.txt
ERR=$OUT/stderr.log
: > "$RES"

git -c advice.detachedHead=false clone -q --branch "v$VERSION" --depth 1 "$REPOSITORY_URL" "$R"
note "tag v$VERSION is $(git -C "$R" rev-parse --short HEAD); machine $(uname -sm)"
make_env "$R/docker/.env" "$DBIMAGE"
published_images "$OUT/published.yaml" "$VERSION"
ports_override "$OUT/ports.yaml" 38080 38081

C=(docker compose -p "$P" --env-file "$R/docker/.env" -f "$R/docker/compose.yaml" -f "$OUT/published.yaml" -f "$OUT/ports.yaml")
API=http://127.0.0.1:38080
MCP=http://127.0.0.1:38081
case $(uname -m) in
  x86_64 | amd64) ARCH=amd64 ;;
  arm64 | aarch64) ARCH=arm64 ;;
  *) ARCH=$(uname -m) ;;
esac

"${C[@]}" pull api mcp migrate retention >> "$ERR" 2>&1
expect "pull exit" "$?" 0
for i in api mcp cli; do
  ref=$REGISTRY/devbuddy-$i:$VERSION
  expect "$i architecture" "$(docker image inspect -f '{{.Architecture}}' "$ref")" "$ARCH"
  expect "$i user" "$(docker image inspect -f '{{.Config.User}}' "$ref")" 1654
  note "$i digest $(docker image inspect -f '{{join .RepoDigests " "}}' "$ref" | grep -oE 'sha256:[0-9a-f]{12}' | head -1)"
done

"${C[@]}" build evidence >> "$ERR" 2>&1
expect "evidence build exit" "$?" 0
"${C[@]}" up -d --no-build >> "$ERR" 2>&1
expect "up exit" "$?" 0
wait_healthy "$P-api-1"
expect "api health" "$(docker inspect -f '{{.State.Health.Status}}' "$P-api-1")" healthy
expect "migrate exit" "$(docker inspect -f '{{.State.ExitCode}}' "$P-migrate-1")" 0
expect "migrations" "$(docker exec "$P-database-1" psql -U devbuddy -d devbuddy -tAc 'select count(*) from "__EFMigrationsHistory"')" "$(migration_count "$R")"
expect "api /health" "$(code $API/health)" 200
expect "api /operations unauthenticated" "$(code $API/operations)" 401
expect "web UI /" "$(code $API/)" 200
expect "mcp POST / unauthenticated" "$(code -X POST -H 'Content-Type: application/json' -d '{}' $MCP/)" 401
expect "mcp GET /no-such-path (control)" "$(code $MCP/no-such-path)" 404
for s in api mcp retention; do
  expect "$s uid" "$(uid_of "$P-$s-1")" 1654
done
expect "database uid" "$(uid_of "$P-database-1")" 70
expect "evidence uid" "$(uid_of "$P-evidence-1")" 1000
for s in api mcp retention evidence; do
  expect "$s read-only" "$(docker inspect -f '{{.HostConfig.ReadonlyRootfs}}' "$P-$s-1")" true
done
"${C[@]}" run --rm -T --no-deps migrate operations --ai 2>>"$ERR" | ai_names > "$OUT/ai-operations.txt"
expect "AI operations from the console image" "$(wc -l < "$OUT/ai-operations.txt" | tr -d ' ')" 20
note "$(tr '\n' ' ' < "$OUT/ai-operations.txt")"
expect "api error lines" "$("${C[@]}" logs --no-log-prefix api 2>&1 | grep -ciE '\b(fail|crit|error)\b')" 0

"${C[@]}" down -v >> "$ERR" 2>&1
expect "volumes left for $P" "$(docker volume ls -q | grep -c "^${P}_")" 0
note "expected names for smoke.sh: $OUT/ai-operations.txt"
finish
