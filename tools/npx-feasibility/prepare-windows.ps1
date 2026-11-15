$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$base = Join-Path $env:USERPROFILE '.devbuddy-npx-test'
$downloads = Join-Path $base 'downloads'
$tools = Join-Path $base 'tools'
New-Item -ItemType Directory -Path $downloads,$tools -Force | Out-Null
$architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if ($architecture -notin @('arm64','x64')) { throw 'Unsupported architecture.' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$checksums = (Invoke-WebRequest -UseBasicParsing 'https://nodejs.org/dist/latest-v24.x/SHASUMS256.txt').Content
$line = @($checksums -split "`n" | Where-Object { $_ -match "  node-v24\.\d+\.\d+-win-$architecture\.zip\s*$" })
if ($line.Count -ne 1) { throw 'Unexpected Node metadata.' }
$parts = $line[0].Trim() -split '\s+'
$filename = $parts[1]
$version = ($filename -split '-')[1]
$archive = Join-Path $downloads $filename
Invoke-WebRequest -UseBasicParsing "https://nodejs.org/dist/$version/$filename" -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $parts[0]) { throw 'Node checksum mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
$nodeDirectory = Join-Path $tools ($filename -replace '\.zip$','')
$env:PATH = "$nodeDirectory;$env:PATH"
$release = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdk = @($release.releases | ForEach-Object { $_.sdks } | Where-Object version -EQ '10.0.400') | Select-Object -First 1
$file = $sdk.files | Where-Object { $_.rid -eq "win-$architecture" -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $file.url.StartsWith('https://builds.dotnet.microsoft.com/')) { throw 'Unexpected SDK metadata.' }
$sdkArchive = Join-Path $downloads 'sdk.zip'
Invoke-WebRequest -UseBasicParsing $file.url -OutFile $sdkArchive
if ((Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash -ne $file.hash) { throw 'SDK checksum mismatch.' }
$dotnetDirectory = Join-Path $tools 'dotnet'
Expand-Archive -LiteralPath $sdkArchive -DestinationPath $dotnetDirectory -Force
$env:DOTNET_ROOT = $dotnetDirectory
$env:PATH = "$dotnetDirectory;$env:PATH"
$bunDirectory = Join-Path $tools 'bun'
$bunPlatform = $(if ($architecture -eq 'arm64') { 'aarch64' } else { 'x64' })
& npm.cmd install --ignore-scripts --prefix $bunDirectory "@oven/bun-windows-$bunPlatform@1.3.14"
if ($LASTEXITCODE -ne 0) { throw 'Bun package installation failed.' }
$bunBin = Join-Path $bunDirectory "node_modules/@oven/bun-windows-$bunPlatform/bin"
$env:PATH = "$bunBin;$env:PATH"
[IO.File]::WriteAllText((Join-Path $base 'tool-path.txt'), $env:PATH)
node --version
& npm.cmd --version
bun --version
dotnet --version
