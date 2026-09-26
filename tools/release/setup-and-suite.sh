#!/usr/bin/env bash
# Release checklist, part 1, before the tag: a clean clone at the commit to be tagged, the full .NET
# suite with Docker available, the format check, the web client's build and suite, and a
# self-contained linux-x64 publish. Runs on a Linux x64 machine with Docker and no SDK: the SDK
# runs in a container, against the clone, mounted at the same path inside and out so the bind
# mounts Testcontainers asks the host daemon for resolve.
#
#   setup-and-suite.sh VERSION COMMIT
#
# DEVBUDDY_RELEASE_WORK  where runs go (default ~/devbuddy-release). On jmhp it was /data/devbuddy-cache/work; on devrelease, the default.
# DEVBUDDY_RELEASE_CACHE NuGet and SDK home, kept between runs (default $DEVBUDDY_RELEASE_WORK/cache).
#
# Never points at an installation's own checkout. Creates $WORK/v$VERSION, and refuses to reuse it.
set -uo pipefail
source "$(dirname "$0")/lib.sh"

VERSION=${1:?version, e.g. 1.7.0}
COMMIT=${2:?commit to be tagged}
WORK=${DEVBUDDY_RELEASE_WORK:-$HOME/devbuddy-release}
CACHE=${DEVBUDDY_RELEASE_CACHE:-$WORK/cache}
BASE=$WORK/v$VERSION
R=$BASE/repo
OUT=$BASE/results
SDK=${DEVBUDDY_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}

fresh_dir "$BASE"
mkdir -p "$OUT" "$CACHE/nuget" "$CACHE/home"
chmod 700 "$OUT"
RES=$OUT/part1-results.txt
: > "$RES"

# Do not add --network host: the API and MCP host tests bind 8080 and 8081, and under host
# networking they collide with any stack on the machine and fail as if the product were broken.
dotnet_in_sdk() {
  docker run --rm -i \
    --add-host host.docker.internal:host-gateway \
    --user "$(id -u):$(id -g)" --group-add "$(getent group docker | cut -d: -f3)" \
    -v /var/run/docker.sock:/var/run/docker.sock \
    -v "$R:$R" -v "$CACHE/nuget:/nuget" -v "$CACHE/home:/dotnethome" -w "$R" \
    -e HOME=/dotnethome -e NUGET_PACKAGES=/nuget -e DOTNET_CLI_HOME=/dotnethome \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
    -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
    "$SDK" dotnet "$@" < /dev/null
}

note "=== v$VERSION part 1: clone"
clone_at "$R" "$COMMIT" || { note "clone failed"; exit 1; }
expect "commit" "$(git -C "$R" rev-parse HEAD)" "$COMMIT.*"
note "$(git -C "$R" log --oneline -1)"
make_env "$R/docker/.env"
expect ".env is ignored by git" "$(git -C "$R" check-ignore -q docker/.env && echo yes || echo no)" yes

note "=== full .NET suite (Release, Linux, Docker available)"
dotnet_in_sdk test DevBuddy.slnx -c Release > "$OUT/suite.log" 2>&1
expect "suite exit" "$?" 0
grep -E "Passed!|Failed!" "$OUT/suite.log" | tee -a "$RES"
note "passed in total: $(grep -oE 'Passed: +[0-9]+' "$OUT/suite.log" | awk '{s+=$2} END{print s+0}')"
grep -E "^\s+Failed " "$OUT/suite.log" | head -20 | tee -a "$RES"

note "=== format"
dotnet_in_sdk format DevBuddy.slnx --verify-no-changes --severity warn > "$OUT/format.log" 2>&1
expect "format exit" "$?" 0

note "=== web build and suite"
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp \
  -v "$R/web/admin:$R/web/admin" -w "$R/web/admin" oven/bun:1 \
  sh -c "bun install --frozen-lockfile && bun run build && bun test" > "$OUT/web.log" 2>&1 < /dev/null
expect "web exit" "$?" 0
grep -E "error TS|\(fail\)|^\s*[0-9]+ (pass|fail)|Ran [0-9]+ tests" "$OUT/web.log" | tail -10 | tee -a "$RES"

note "=== self-contained publish, linux-x64 console"
dotnet_in_sdk publish src/hosts/DevBuddy.Cli -c Release -r linux-x64 --self-contained true \
  -o "$R/artifacts/publish/linux-x64/cli" > "$OUT/publish-linux-x64.log" 2>&1
expect "publish exit" "$?" 0
expect "tracked files changed by the run" "$(git -C "$R" status --short | grep -vc '^?? artifacts/')" 0

finish
