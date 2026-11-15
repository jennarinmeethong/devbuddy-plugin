param(
    [string]$Image = 'node@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53'
)

# Local test orchestration. Only this client's compiled/package/test-tool files are mounted.
# No Docker socket, credentials or live application volumes enter the disposable container.
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run = Join-Path $project ('out/musl-smoke-' + [Guid]::NewGuid().ToString('D'))
$bundle = Join-Path $run 'input'
$evidence = Join-Path $run 'evidence'
New-Item -ItemType Directory -Path $bundle, $evidence -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'package.json'), (Join-Path $project 'README.md'), (Join-Path $project 'dist') -Destination $bundle -Recurse
New-Item -ItemType Directory -Path (Join-Path $bundle 'tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package-smoke.mjs'), (Join-Path $PSScriptRoot 'client-smoke.mjs') -Destination (Join-Path $bundle 'tools')
$dockerArguments = @(
    'run', '--rm', '--user', 'node', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
    '--read-only', '--tmpfs', '/work:rw,exec,uid=1000,gid=1000,mode=0700',
    '--tmpfs', '/tmp:rw,exec,uid=1000,gid=1000,mode=0700',
    '--mount', "type=bind,source=$bundle,target=/input,readonly",
    '--mount', "type=bind,source=$evidence,target=/evidence",
    '--env', 'HOME=/work', '--env', 'npm_config_cache=/work/npm-cache',
    '--workdir', '/work', $Image, 'sh', '-c',
    'cp -R /input/. /work/ && node --version && ldd --version 2>&1; node tools/package-smoke.mjs; status=$?; if [ -d out ]; then cp -R out/. /evidence/; fi; exit "$status"'
)
& docker @dockerArguments
if ($LASTEXITCODE -ne 0) { throw "Musl package drill failed. Evidence: $evidence" }
Write-Output "Musl package drill passed. Pinned image: $Image. Evidence: $evidence"
