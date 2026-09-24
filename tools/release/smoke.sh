#!/bin/sh
# Release checklist, after the tag: smoke-test one published archive where its platform runs.
# Starts the self-contained console: its AI catalogue must be the published console image's, in
# order, and "retention --every 24" must be refused with exit 2 rather than read as 24 days.
#
#   smoke.sh ARCHIVE EXPECTED [IMAGE]
#
# EXPECTED is post-images.sh's results/ai-operations.txt. With IMAGE, the test runs inside that
# container instead, after installing what the release matrix says the platform needs: ubuntu:24.04
# for linux-x64 and linux-arm64, alpine:3 for the musl RIDs. Plain POSIX sh, so it runs on macOS.
set -u
ARCHIVE=${1:?archive}
EXPECTED=${2:?expected names}
IMAGE=${3:-}

if [ -n "$IMAGE" ]; then
  HERE=$(cd "$(dirname "$0")" && pwd)
  A=$(cd "$(dirname "$ARCHIVE")" && pwd)/$(basename "$ARCHIVE")
  E=$(cd "$(dirname "$EXPECTED")" && pwd)/$(basename "$EXPECTED")
  exec docker run --rm -v "$HERE:/tools:ro" -v "$A:/in/archive.tar.gz:ro" -v "$E:/in/expected.txt:ro" \
    "$IMAGE" sh -c '
      if command -v apk > /dev/null; then apk add --no-cache -q libstdc++ libgcc icu-libs > /dev/null
      elif command -v apt-get > /dev/null; then
        apt-get update -qq > /dev/null
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
          $(apt-cache search --names-only "^libicu[0-9]+$" | cut -d" " -f1 | head -1) > /dev/null
      fi
      cat /etc/os-release 2>/dev/null | grep -E "^PRETTY_NAME="
      sh /tools/smoke.sh /in/archive.tar.gz /in/expected.txt'
fi

FAILED=0
check() { # label actual wanted
  if [ "$2" = "$3" ]; then echo "ok   $1: $2"; else echo "FAIL $1: $2 (wanted $3)"; FAILED=$((FAILED + 1)); fi
}

DIR=$(mktemp -d)
tar xzf "$ARCHIVE" -C "$DIR"
CLI="$DIR/Cli/DevBuddy.Cli"
echo "platform $(uname -sm)"
"$CLI" operations --ai > "$DIR/ops.txt" 2> "$DIR/ops.err"
check "operations --ai exit" "$?" 0
awk '$NF=="ai"{print $1}' "$DIR/ops.txt" > "$DIR/names.txt"
echo "names $(wc -l < "$DIR/names.txt" | tr -d ' ')"
check "identical to the console image" "$(cmp -s "$DIR/names.txt" "$EXPECTED" && echo yes || echo no)" yes
"$CLI" retention --every 24 > "$DIR/every.txt" 2>&1
check "retention --every 24 exit" "$?" 2
echo "  $(head -c 160 "$DIR/every.txt" | tr '\n' ' ')"
rm -rf "$DIR"
echo "=== $FAILED failed"
[ "$FAILED" -eq 0 ]
