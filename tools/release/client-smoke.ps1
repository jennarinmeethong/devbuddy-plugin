# Release checklist, after the tag: the client smoke test of a Windows archive (info.md, 2026-09-24).
# The console, and the MCP server over stdio as a plugin starts it, from the published archive.
# Needs no database, which is a client machine's situation, and changes nothing outside -Work.
# Owed for win-arm64 every release; it runs unchanged against win-x64.
#
#   powershell -ExecutionPolicy Bypass -File client-smoke.ps1 -Archive <tar.gz> -Expected <names> -Work <dir>
#
# -Expected is post-images.sh's results/ai-operations.txt. Exits 1 when any check failed.
#
# Do not name a variable $expected in this script: PowerShell variables are case-insensitive, so it
# would be the -Expected parameter, and an array assigned to it is flattened into one string.
param(
  [Parameter(Mandatory = $true)][string]$Archive,
  [Parameter(Mandatory = $true)][string]$Expected,
  [Parameter(Mandatory = $true)][string]$Work
)
$ErrorActionPreference = 'Continue'
$script:failed = 0
function Check([string]$label, $actual, $wanted) {
  if ("$actual" -eq "$wanted") { "ok   ${label}: $actual" }
  else { "FAIL ${label}: $actual (wanted $wanted)"; $script:failed++ }
}

if (Test-Path $Work) { "$Work already exists, refusing to reuse it"; exit 1 }
New-Item -ItemType Directory -Force $Work | Out-Null
tar -xzf $Archive -C $Work
$cli = Join-Path $Work 'Cli\DevBuddy.Cli.exe'
$mcp = Join-Path $Work 'McpServer\DevBuddy.McpServer.exe'
"os $([Environment]::OSVersion.Version) arch $env:PROCESSOR_ARCHITECTURE"

# PE machine of both apphosts must be this machine's: 0xAA64 is ARM64, 0x8664 is x64.
$machine = @{ 'ARM64' = 'AA64'; 'AMD64' = '8664' }[$env:PROCESSOR_ARCHITECTURE]
foreach ($exe in @($cli, $mcp)) {
  $b = [IO.File]::ReadAllBytes($exe); $pe = [BitConverter]::ToInt32($b, 0x3C)
  Check "$(Split-Path $exe -Leaf) PE machine" ('{0:X4}' -f [BitConverter]::ToUInt16($b, $pe + 4)) $machine
}

$catalogue = @(Get-Content $Expected | Where-Object { $_.Trim() })
$env:DEVBUDDY_ConnectionStrings__DevBuddy = $null
$out = & $cli operations --ai 2>$null
Check 'console operations --ai exit' $LASTEXITCODE 0
$names = @($out | Where-Object { $_.TrimEnd().EndsWith(' ai') } | ForEach-Object { $_.Trim().Split(' ')[0] })
"console names $($names.Count)"
Check 'identical to the console image' (($names -join ',') -eq ($catalogue -join ',')) $true
if (($names -join ',') -ne ($catalogue -join ',')) {
  Compare-Object $names $catalogue -SyncWindow 0 | Select-Object -First 3 | ForEach-Object { "  diff [$($_.InputObject)] $($_.SideIndicator)" }
}
& $cli retention --every 24 > $null 2>&1
Check 'console retention --every 24 exit' $LASTEXITCODE 2
& $cli migrate > $null 2>&1
Check 'console migrate with no connection string exit' $LASTEXITCODE 2

# The MCP server over stdio, pointed at a database that is not there, as a client machine is. The
# connection string carries no password: nothing connects before a tool is called.
$psi = New-Object Diagnostics.ProcessStartInfo $mcp, '--stdio'
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$psi.EnvironmentVariables['DEVBUDDY_ConnectionStrings__DevBuddy'] = 'Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=3'
$p = [Diagnostics.Process]::Start($psi)
$errTask = $p.StandardError.ReadToEndAsync()
$lines = New-Object Collections.Generic.List[string]
function Send($m) { $p.StandardInput.WriteLine(($m | ConvertTo-Json -Compress -Depth 10)); $p.StandardInput.Flush() }
function Await($id) {
  while ($true) {
    $l = $p.StandardOutput.ReadLine()
    if ($null -eq $l) { throw "stdout closed waiting for $id" }
    $lines.Add($l); $m = $l | ConvertFrom-Json
    if ($m.id -eq $id) { return $m }
  }
}
Send @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'client-smoke'; version = '0' } } }
$init = Await 1
Check 'mcp initialize, server name' $init.result.serverInfo.name 'devbuddy'
Send @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
Send @{ jsonrpc = '2.0'; id = 2; method = 'tools/list' }
$tools = @((Await 2).result.tools | ForEach-Object { $_.name } | Sort-Object)
"mcp tools $($tools.Count)"
Check 'mcp tools, same names as the console image' (($tools -join ',') -eq (($catalogue | Sort-Object) -join ',')) $true
$p.StandardInput.Close()
if (-not $p.WaitForExit(30000)) { $p.Kill(); Check 'mcp exits when its input closes' 'no' 'yes' }
else { Check 'mcp exit after its input closed' $p.ExitCode 0 }
$rest = $p.StandardOutput.ReadToEnd(); if ($rest) { $rest.Split("`n") | Where-Object { $_.Trim() } | ForEach-Object { $lines.Add($_) } }
$bad = @($lines | Where-Object { try { $null = $_ | ConvertFrom-Json; -not ($_ -match '"jsonrpc"') } catch { $true } })
"mcp stdout lines $($lines.Count)"
Check 'every stdout line is JSON-RPC' ($bad.Count -eq 0) $true

"=== $script:failed failed"
if ($script:failed -gt 0) { exit 1 }
