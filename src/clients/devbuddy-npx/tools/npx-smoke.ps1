param([switch]$Native)

# Local-artifact test only: no npm publication, global installation, or real workspace token.
$ErrorActionPreference = 'Stop'
$packageDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runDirectory = Join-Path $packageDirectory ('out/npx-smoke-' + [Guid]::NewGuid().ToString('D'))
$cleanDirectory = Join-Path $runDirectory 'clean-folder-ภาษาไทย'
$cacheDirectory = Join-Path $runDirectory 'npm-cache'
New-Item -ItemType Directory -Path $cleanDirectory -Force | Out-Null
$oldIgnoreScripts = $env:npm_config_ignore_scripts
$env:npm_config_ignore_scripts = 'true'
try {
    $feasibilityDirectory = Join-Path $runDirectory 'feasibility'
    node (Join-Path $PSScriptRoot 'prepare-feasibility.mjs') $feasibilityDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Feasibility package preparation failed.' }
    Push-Location $feasibilityDirectory
    try {
        $packed = npm pack --ignore-scripts --json --pack-destination $runDirectory | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw 'npm pack failed.' }
        if (@($packed.files | Where-Object { $_.path -match '(^|/)(node_modules|tests|tools|out)/|\.env$' }).Count -ne 0) {
            throw 'Unexpected file in the prototype tarball.'
        }
        $archive = Join-Path $runDirectory $packed.filename
        $packed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'package.json') -Encoding utf8
    } finally { Pop-Location }

    Push-Location $cleanDirectory
    try {
        foreach ($pass in @('cold', 'warm')) {
            $help = npx --yes --ignore-scripts --cache $cacheDirectory --package $archive devbuddy-credential-probe --help
            if ($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch 'DevBuddy feasibility probe') {
                throw "npx $pass help failed."
            }
            $help | Set-Content -LiteralPath (Join-Path $runDirectory "$pass-help.txt") -Encoding utf8
            if ($Native) {
                $output = npx --yes --ignore-scripts --cache $cacheDirectory --package $archive devbuddy-credential-probe roundtrip
                if ($LASTEXITCODE -ne 0) { throw "npx $pass native probe failed." }
                $result = $output | ConvertFrom-Json
                if ($result.passed -ne $true -or $result.stage -ne 'roundtrip') { throw 'Invalid probe response.' }
                $nativeFiles = @($result.nativeFiles | Where-Object { $_.path -like '*.node' })
                if ($nativeFiles.Count -eq 0) { throw 'No loaded native module was reported.' }
                foreach ($file in $nativeFiles) {
                    $nativePath = $file.path.Replace('\\?\', '')
                    if (-not $nativePath.StartsWith($cacheDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                        throw 'The probe loaded a native module outside the isolated npm cache.'
                    }
                }
                $output | Set-Content -LiteralPath (Join-Path $runDirectory "$pass-native.json") -Encoding utf8
            }
        }
        # Invalid arguments must fail before touching any credential. Neither output stream
        # may echo the supplied marker, which stands in for accidentally pasted secret data.
        foreach ($case in @('invalid-stage', 'invalid-id')) {
            $marker = 'synthetic-sensitive-argument-' + [Guid]::NewGuid().ToString('N')
            $errorFile = Join-Path $runDirectory "$case.stderr.txt"
            $probeArguments = $(if ($case -eq 'invalid-stage') { @($marker) } else { @('roundtrip', $marker) })
            $invalidOutput = npx --yes --ignore-scripts --cache $cacheDirectory --package $archive devbuddy-credential-probe @probeArguments 2> $errorFile
            $invalidExit = $LASTEXITCODE
            $errorText = Get-Content -LiteralPath $errorFile -Raw
            if ($invalidExit -ne 1 -or ($invalidOutput -join '').Length -ne 0) {
                throw "The $case probe did not fail cleanly with empty stdout."
            }
            if ($errorText.Contains($marker) -or $errorText -notmatch 'DevBuddy credential probe failed') {
                throw "The $case diagnostic echoed input or lacked a safe error message."
            }
        }
    } finally { Pop-Location }
    [ordered]@{
        passed = $true
        native = [bool]$Native
        artifactSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        ignoredInstallScripts = $true
        invalidStageAndId = 'PASSED'
        argumentEchoProtection = 'PASSED'
        sacCompatible = 'UNVERIFIED_BY_THIS_TOOL'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding utf8
    Write-Output "npx cold/warm artifact probes passed. Evidence: $runDirectory"
} finally {
    if ($null -eq $oldIgnoreScripts) { Remove-Item Env:npm_config_ignore_scripts -ErrorAction SilentlyContinue }
    else { $env:npm_config_ignore_scripts = $oldIgnoreScripts }
}
