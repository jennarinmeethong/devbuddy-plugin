param(
    [string]$TestRoot = (Join-Path $HOME '.devbuddy-npx-test/production-01b838f7afb5486db45cfe02219a0b24'),
    [string]$NodePath
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
$base = Join-Path $HOME '.devbuddy-npx-test'
$toolPath = Join-Path $base 'tool-path.txt'
if (Test-Path -LiteralPath $toolPath) { $env:PATH = [IO.File]::ReadAllText($toolPath).Trim() }
if ($NodePath) { $env:PATH = (Split-Path -Parent $NodePath) + ';' + $env:PATH }
$dotnetRoot = Join-Path $base 'tools/dotnet'
if (Test-Path -LiteralPath $dotnetRoot) { $env:DOTNET_ROOT = $dotnetRoot }
$env:DEVBUDDY_TEST_DOTNET_DLL = Join-Path $TestRoot 'src/clients/DevBuddy.Client/bin/Release/net10.0/devbuddy.dll'
$env:DEVBUDDY_TEST_NATIVE = '1'
$env:DEVBUDDY_NPX_TEST_TIMEOUT_MS = '300000'
Set-Location -LiteralPath (Join-Path $TestRoot 'src/clients/devbuddy-npx')
$report = & node tools/package-smoke.mjs
if ($LASTEXITCODE -ne 0) { throw 'Production native package test failed.' }
[IO.File]::WriteAllText((Join-Path (Get-Location).Path 'production-native-package.json'), ($report -join "`n"))
$installed = ($report -join "`n" | ConvertFrom-Json).installedEntry
$inventory = & node tools/native-inventory.mjs $installed
if ($LASTEXITCODE -ne 0) { throw 'Installed native inventory failed.' }
[IO.File]::WriteAllText((Join-Path (Get-Location).Path 'production-native-inventory.json'), ($inventory -join "`n"))
Write-Output 'Production native/installed/.NET interchange checks passed. SAC policy must be verified separately.'
