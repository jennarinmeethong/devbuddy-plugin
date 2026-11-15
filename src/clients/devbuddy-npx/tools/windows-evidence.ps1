param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../out/windows-feasibility'),
    [switch]$IncludeCompatibility,
    [string]$CredentialPeerAssembly
)

# Test/release tooling only. Never enables/disables SAC or changes machine policy.
$ErrorActionPreference = 'Stop'
function Get-SacPolicySnapshot {
    try {
        $raw = & "$env:SystemRoot/System32/citool.exe" -lp -json
        $nativeExit = $LASTEXITCODE
        $data = $raw | ConvertFrom-Json
        if ($nativeExit -ne 0 -or $null -eq $data.Policies) {
            return @{ available = $false; enforced = $false; policies = @() }
        }
        $policies = @($data.Policies | Where-Object { $_.FriendlyName -like 'VerifiedAndReputable*' } |
            Select-Object PolicyID, FriendlyName, IsEnforced)
        $enforced = @($policies | Where-Object { $_.FriendlyName -eq 'VerifiedAndReputableDesktop' -and [string]$_.IsEnforced -eq 'True' }).Count -gt 0
        return @{ available = $true; enforced = $enforced; policies = $policies }
    } catch {
        return @{ available = $false; enforced = $false; policies = @() }
    }
}
$packageDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidenceDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$started = Get-Date
$policyPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy'
$stateBefore = (Get-ItemProperty -LiteralPath $policyPath -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
$policyBefore = Get-SacPolicySnapshot
$windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$entry = Join-Path $packageDirectory 'dist/probe-main.js'
$nodePath = (Get-Command node -CommandType Application | Select-Object -First 1).Source
$probeOutput = & $nodePath $entry roundtrip 2> (Join-Path $evidenceDirectory 'probe.stderr.txt')
$probeExit = $LASTEXITCODE
$probeOutput | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'probe.json') -Encoding utf8

$compatibilityExit = $null
if ($IncludeCompatibility) {
    $compatibilityArguments = @((Join-Path $PSScriptRoot 'compatibility.mjs'))
    if ($CredentialPeerAssembly) { $compatibilityArguments += $CredentialPeerAssembly }
    & $nodePath @compatibilityArguments 1> (Join-Path $evidenceDirectory 'compatibility.json') 2> (Join-Path $evidenceDirectory 'compatibility.stderr.txt')
    $compatibilityExit = $LASTEXITCODE
}

# Include installed .node files even if SAC prevented them from being loaded.
$paths = @($nodePath)
$nativeDirectory = Join-Path $packageDirectory 'node_modules/@koromix'
if (Test-Path -LiteralPath $nativeDirectory) {
    $paths += @(Get-ChildItem -LiteralPath $nativeDirectory -Recurse -File -Filter '*.node' | ForEach-Object FullName)
}
$inventory = @($paths | ForEach-Object {
    $signature = Get-AuthenticodeSignature -LiteralPath $_
    [ordered]@{
        path = $_
        sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        signatureStatus = [string]$signature.Status
        signer = $signature.SignerCertificate.Subject
    }
})
$eventsAvailable = $true
try {
    $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; StartTime = $started } -ErrorAction Stop |
        Where-Object { $_.Message -match 'koffi|probe-main|node\.exe' } |
        Select-Object Id, TimeCreated, LevelDisplayName, Message)
} catch {
    $events = @()
    $eventsAvailable = $false
}
$stateAfter = (Get-ItemProperty -LiteralPath $policyPath -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
$policyAfter = Get-SacPolicySnapshot
$report = [ordered]@{
    started = $started.ToUniversalTime().ToString('o')
    windowsBuild = "$($windows.CurrentBuild).$($windows.UBR)"
    displayVersion = $windows.DisplayVersion
    architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    node = (& $nodePath --version)
    npm = (& npm --version)
    koffi = '3.3.3'
    lockSha256 = (Get-FileHash -LiteralPath (Join-Path $packageDirectory 'bun.lock') -Algorithm SHA256).Hash.ToLowerInvariant()
    sacStateBefore = $stateBefore
    sacStateAfter = $stateAfter
    sacPolicyBefore = $policyBefore
    sacPolicyAfter = $policyAfter
    probeExit = $probeExit
    compatibilityExit = $compatibilityExit
    nativeFiles = $inventory
    codeIntegrityEventsAvailable = $eventsAvailable
    codeIntegrityEvents = $events
    # Even enforcement success here is only a prototype result, not a released-package claim.
    sacPrototypeResult = $(if ($stateBefore -eq 1 -and $stateAfter -eq 1 -and $policyBefore.enforced -and $policyAfter.enforced -and $probeExit -eq 0) { 'PASSED_ON_THIS_MACHINE' } else { 'UNVERIFIED_OR_FAILED' })
    sacReleaseCompatible = 'UNVERIFIED'
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'environment.json') -Encoding utf8
Write-Output "Evidence: $evidenceDirectory; probe exit: $probeExit; SAC state: $stateBefore -> $stateAfter"
if ($probeExit -ne 0 -or ($IncludeCompatibility -and $compatibilityExit -ne 0)) { exit 1 }
