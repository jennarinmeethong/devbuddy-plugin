#!/bin/sh
# Native-architecture, compiler-free artifact drill. No live application mounts.
set -eu
project=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
image=${DEVBUDDY_TEST_MUSL_IMAGE:-node@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53}
architecture=$(docker version --format '{{.Server.Arch}}')
case "$architecture" in amd64|arm64) ;; *) printf 'Unsupported test engine architecture\n' >&2; exit 1 ;; esac
user_id=$(id -u)
group_id=$(id -g)
if [ "$user_id" -eq 0 ]; then printf 'Run this drill as a non-root user\n' >&2; exit 1; fi
mkdir -p "$project/out"
run=$(mktemp -d "$project/out/musl-native-XXXXXX")
mkdir -p "$run/input/tools" "$run/evidence"
cp "$project/package.json" "$project/README.md" "$run/input/"
cp -R "$project/dist" "$run/input/"
cp "$project/tools/package-smoke.mjs" "$project/tools/client-smoke.mjs" "$run/input/tools/"
docker run --rm --platform "linux/$architecture" --user "$user_id:$group_id" --cap-drop ALL --security-opt no-new-privileges \
  --read-only --tmpfs "/work:rw,exec,uid=$user_id,gid=$group_id,mode=0700" \
  --tmpfs "/tmp:rw,exec,uid=$user_id,gid=$group_id,mode=0700" \
  --mount "type=bind,source=$run/input,target=/input,readonly" \
  --mount "type=bind,source=$run/evidence,target=/evidence" \
  --env HOME=/work --env npm_config_cache=/work/npm-cache --workdir /work "$image" sh -c '
    for tool in dotnet cc gcc g++ clang; do
      if command -v "$tool" >/dev/null 2>&1; then echo "Unexpected SDK/compiler in test image" >&2; exit 1; fi
    done
    cp -R /input/. /work/ || exit 1
    node --version
    node tools/package-smoke.mjs
    status=$?
    if [ -d out ]; then cp -R out/. /evidence/; fi
    exit "$status"
  '
printf 'Native %s musl checks passed; image %s; evidence %s\n' "$architecture" "$image" "$run/evidence"
