param([Parameter(Mandatory = $true)][string]$TestRoot)
$ErrorActionPreference = 'Stop'
function Invoke-Checked([string]$Executable, [string[]]$Arguments, [string]$Log) {
    $previous = $ErrorActionPreference
    try {
        # Windows PowerShell turns native stderr banners into error records.
        # Check the exit code rather than treating Bun's banner as failure.
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments *> $Log
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    if ($exitCode -ne 0) { throw 'Test command failed; inspect its log.' }
}
$env:PATH = [IO.File]::ReadAllText((Join-Path $HOME '.devbuddy-npx-test/tool-path.txt')).Trim()
Set-Location -LiteralPath $TestRoot
Invoke-Checked 'dotnet' @('build', 'src/clients/DevBuddy.Client', '-c', 'Release') 'dotnet-build.txt'
$env:DEVBUDDY_TEST_DOTNET_DLL = Join-Path $TestRoot 'src/clients/DevBuddy.Client/bin/Release/net10.0/devbuddy.dll'
Set-Location -LiteralPath (Join-Path $TestRoot 'src/clients/devbuddy-npx')
Invoke-Checked 'bun' @('install', '--frozen-lockfile', '--ignore-scripts') 'install.txt'
Invoke-Checked 'bun' @('run', 'typecheck') 'typecheck.txt'
Invoke-Checked 'bun' @('run', 'build') 'build.txt'
Invoke-Checked 'bun' @('test', 'tests') 'unit-tests.txt'
foreach ($pair in @(@('client-smoke.mjs', 'client-result.json'), @('package-smoke.mjs', 'package-result.json'))) {
    $report = & node (Join-Path 'tools' $pair[0])
    if ($LASTEXITCODE -ne 0) { throw 'Production client smoke failed.' }
    [IO.File]::WriteAllText((Join-Path (Get-Location).Path $pair[1]), ($report -join "`n"))
}
Write-Output 'Production file-store checks passed on Windows ARM64.'
