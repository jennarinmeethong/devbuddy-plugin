#!/bin/sh
# Lists the MCP server sessions an upgrade left on the old image (Phase 14, A3).
#
# A plugin that reaches the stack over stdio starts one container per assistant session with
# `docker compose run --rm -T mcp --stdio`. Each run takes the image the service has at that moment,
# and the container lives exactly as long as the session: it is removed when the session closes its
# input. So after an upgrade, every session that was already open keeps answering from the old
# image, and a session opened afterwards gets the new one. Nothing reuses an old image; it is only
# never replaced under a running session.
#
#   stale-sessions.sh [COMPOSE_PROJECT]      (default: devbuddy, the name compose.yaml sets)
#
# Prints one line per session on an image other than the running mcp service's, then a count.
# Changes nothing. The fix is to restart those assistant sessions; stopping a container instead
# cuts an assistant off in the middle of whatever it was doing.
set -u
PROJECT=${1:-devbuddy}

service=$(docker ps -q --filter "label=com.docker.compose.project=$PROJECT" \
  --filter label=com.docker.compose.service=mcp --filter label=com.docker.compose.oneoff=False | head -1)
[ -n "$service" ] || { echo "no running mcp service in Compose project $PROJECT" >&2; exit 2; }
current=$(docker inspect -f '{{.Image}}' "$service")

total=0
stale=0
for c in $(docker ps -q --filter "label=com.docker.compose.project=$PROJECT" \
  --filter label=com.docker.compose.service=mcp --filter label=com.docker.compose.oneoff=True); do
  total=$((total + 1))
  set -- $(docker inspect -f '{{.Name}} {{.Image}} {{.Created}}' "$c")
  if [ "$2" != "$current" ]; then
    stale=$((stale + 1))
    echo "stale ${1#/}, started $3, image $(echo "${2#sha256:}" | cut -c1-12)"
  fi
done
echo "sessions $total, on an older image than the mcp service $stale"
