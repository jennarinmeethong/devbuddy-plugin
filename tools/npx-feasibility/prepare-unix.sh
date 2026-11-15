#!/usr/bin/env bash
set -euo pipefail
fetch() {
  if command -v curl >/dev/null; then curl -fsSL "$1" -o "$2";
  else wget -q "$1" -O "$2"; fi
}

# Isolated user-owned toolchain; no sudo, shell-profile changes, or machine policy changes.
base="$HOME/.devbuddy-npx-test"
mkdir -p "$base/downloads" "$base/tools/node" "$base/tools/dotnet"
cd "$base/downloads"
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) node_platform=darwin-arm64; sdk_rid=osx-arm64; bun_platform=darwin-aarch64 ;;
  Linux-aarch64) node_platform=linux-arm64; sdk_rid=linux-arm64; bun_platform=linux-aarch64 ;;
  Linux-x86_64) node_platform=linux-x64; sdk_rid=linux-x64; bun_platform=linux-x64 ;;
  *) echo 'Unsupported preparation platform' >&2; exit 1 ;;
esac
fetch https://nodejs.org/dist/latest-v24.x/SHASUMS256.txt node-shasums.txt
node_file="$(awk -v suffix="-${node_platform}.tar.gz" 'index($2,suffix) > 0 && index($2,suffix) == length($2)-length(suffix)+1 { print $2 }' node-shasums.txt)"
[[ "$node_file" =~ ^node-v24\.[0-9]+\.[0-9]+-(darwin|linux)-(arm64|x64)\.tar\.gz$ ]] || exit 1
node_version="${node_file#node-}"
node_version="${node_version%-$node_platform.tar.gz}"
fetch "https://nodejs.org/dist/$node_version/$node_file" "$node_file"
grep "  $node_file$" node-shasums.txt > node-selected.sha256
if command -v sha256sum >/dev/null; then sha256sum -c node-selected.sha256; else shasum -a 256 -c node-selected.sha256; fi
tar -xzf "$node_file" --strip-components=1 -C "$base/tools/node"
export PATH="$base/tools/node/bin:$HOME/.bun/bin:$PATH"
fetch https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json dotnet-releases.json
node --input-type=module - "$sdk_rid" <<'JS'
import fs from 'node:fs';
const releases = JSON.parse(fs.readFileSync('dotnet-releases.json')).releases;
const sdk = releases.flatMap(r => r.sdks ?? [r.sdk]).find(s => s?.version === '10.0.400');
const file = sdk?.files.find(f => f.rid === process.argv[2] && f.name.endsWith('.tar.gz'));
if (!file || !file.url.startsWith('https://builds.dotnet.microsoft.com/')) throw Error('Unexpected SDK metadata');
fs.writeFileSync('sdk-url.txt', file.url);
fs.writeFileSync('sdk.sha512', `${file.hash.toLowerCase()}  sdk.tar.gz\n`);
JS
fetch "$(cat sdk-url.txt)" sdk.tar.gz
if command -v sha512sum >/dev/null; then sha512sum -c sdk.sha512; else shasum -a 512 -c sdk.sha512; fi
tar -xzf sdk.tar.gz -C "$base/tools/dotnet"
export DOTNET_ROOT="$base/tools/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
if ! command -v bun >/dev/null; then
  npm install --ignore-scripts --prefix "$base/tools/bun" "@oven/bun-$bun_platform@1.3.14"
  export PATH="$base/tools/bun/node_modules/@oven/bun-$bun_platform/bin:$PATH"
fi
node --version
npm --version
bun --version
dotnet --version
printf '%s\n' "$PATH" > "$base/tool-path.txt"
