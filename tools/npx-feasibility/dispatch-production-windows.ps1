param([Parameter(Mandatory = $true)][string]$TestRoot, [string]$NodePath)
$ErrorActionPreference = 'Stop'
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { throw 'No desktop session is available for native credential tests.' }
$root = (Resolve-Path -LiteralPath $TestRoot).Path
$evidence = Join-Path $root ('native-evidence-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
function Save-Json($Value, [string]$File) {
    [IO.File]::WriteAllText($File, ($Value | ConvertTo-Json -Depth 12))
}
function Policy-Snapshot {
    $snapshot = @{ utc = [DateTime]::UtcNow.ToString('O'); available = $false; enforced = $false }
    try {
        $raw = & "$env:SystemRoot/System32/citool.exe" -lp -json 2>$null
        $exitCode = $LASTEXITCODE
        $data = ($raw -join "`n") | ConvertFrom-Json
        if ($exitCode -eq 0 -and $null -ne $data.Policies) {
            $snapshot.available = $true
            $snapshot.policies = @($data.Policies | Where-Object { $_.FriendlyName -like 'VerifiedAndReputable*' } | Select-Object FriendlyName, IsEnforced, PolicyID)
            $snapshot.enforced = @($snapshot.policies | Where-Object { $_.FriendlyName -eq 'VerifiedAndReputableDesktop' -and [string]$_.IsEnforced -eq 'True' }).Count -gt 0
        }
    } catch { }
    return $snapshot
}
$before = Policy-Snapshot
Save-Json $before (Join-Path $evidence 'policy-before.json')
$taskName = 'DevBuddyNpxProduction-' + [Guid]::NewGuid().ToString('N')
$runner = Join-Path $root 'tools/npx-feasibility/run-production-windows-desktop.ps1'
# This owned local test script needs no unrestricted policy. Scope RemoteSigned to the
# child process; machine/user/managed policies and native SAC enforcement are unchanged.
$arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File `"$runner`" -TestRoot `"$root`""
if ($NodePath) { $arguments += " -NodePath `"$NodePath`"" }
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe" -Argument $arguments
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 45)
$registered = $false
$started = [DateTime]::UtcNow
try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null
    $registered = $true
    Start-ScheduledTask -TaskName $taskName
    Write-Output 'Started the limited interactive-user production credential test.'
    $deadline = (Get-Date).AddMinutes(35)
    do {
        Start-Sleep -Seconds 2
        $info = Get-ScheduledTaskInfo -TaskName $taskName
        $state = (Get-ScheduledTask -TaskName $taskName).State
        $ran = $info.LastRunTime.ToUniversalTime() -ge $started.AddSeconds(-2)
    } while ((-not $ran -or $state -eq 'Running') -and (Get-Date) -lt $deadline)
    if (-not $ran -or $state -eq 'Running' -or $info.LastTaskResult -ne 0) { throw 'Native desktop test failed or did not finish. Inspect the isolated reports.' }
    $finished = [DateTime]::UtcNow
    $client = Join-Path $root 'src/clients/devbuddy-npx'
    $package = Get-Content -LiteralPath (Join-Path $client 'production-native-package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $inventory = Get-Content -LiteralPath (Join-Path $client 'production-native-inventory.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $package.passed -or -not $package.native) { throw 'No passing production native package result.' }
    $signatures = @($inventory.nativeFiles | ForEach-Object {
        $signature = Get-AuthenticodeSignature -LiteralPath $_.path
        @{ path = $_.path; sha256 = $_.sha256; status = [string]$signature.Status }
    })
    Save-Json $package (Join-Path $evidence 'package.json')
    Save-Json $inventory (Join-Path $evidence 'inventory.json')
    Save-Json $signatures (Join-Path $evidence 'signatures.json')
    $after = Policy-Snapshot
    Save-Json $after (Join-Path $evidence 'policy-after.json')
    # Parse offsets explicitly: Windows PowerShell's DateTime cast converts a Z
    # string to local time, which must not be compared to an unconverted UTC value.
    $beforeUtc = [DateTimeOffset]::Parse([string]$before.utc).UtcDateTime
    $afterUtc = [DateTimeOffset]::Parse([string]$after.utc).UtcDateTime
    $bracketed = $beforeUtc -le $started -and $finished -le $afterUtc
    Save-Json @{ passed = $true; startedUtc = $started.ToString('O'); finishedUtc = $finished.ToString('O'); taskRunLevel = 'Limited'; taskResult = $info.LastTaskResult; policySnapshotsBracketRun = $bracketed; sacEnforcedDuringRun = ($bracketed -and $before.available -and $after.available -and $before.enforced -and $after.enforced) } (Join-Path $evidence 'run.json')
    Write-Output "Production native tests passed. Evidence: $evidence"
} finally {
    if ($registered) {
        if ((Get-ScheduledTask -TaskName $taskName).State -eq 'Running') { Stop-ScheduledTask -TaskName $taskName }
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}
