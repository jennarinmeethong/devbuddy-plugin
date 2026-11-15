$ErrorActionPreference = 'Stop'
$base = Join-Path $env:USERPROFILE '.devbuddy-npx-test'
$env:PATH = [IO.File]::ReadAllText((Join-Path $base 'tool-path.txt'))
$env:DOTNET_ROOT = Join-Path $base 'tools/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Set-Location (Join-Path $base 'work/src/clients/devbuddy-npx')
node tools/compatibility.mjs ../../../tools/npx-feasibility/CredentialPeer/bin/Release/net10.0/CredentialPeer.dll > (Join-Path $base 'desktop-compatibility.json')
if ($LASTEXITCODE -ne 0) { throw 'Desktop interoperability failed; inspect the JSON result.' }
$peer = Join-Path $base 'work/tools/npx-feasibility/CredentialPeer/bin/Release/net10.0/CredentialPeer.dll'
./tools/windows-evidence.ps1 -IncludeCompatibility -CredentialPeerAssembly $peer -OutputDirectory (Join-Path $base 'desktop-windows-evidence')
if ($LASTEXITCODE -ne 0) { throw 'Windows credential evidence probe failed.' }
node tools/npx-smoke.mjs --native
if ($LASTEXITCODE -ne 0) { throw 'Packed npx native probe failed.' }
Write-Output 'Desktop credential and packed-artifact probes passed. SAC status is recorded separately.'
