<#
.SYNOPSIS
Loads this root's DevBuddy settings into the current session. Dot-source it.

.DESCRIPTION
The isolation boundary is the process, not the directory. Whichever token is in the environment
when the assistant is launched is the identity every tool call runs as, no matter which folder is
open at the time. So: one session per root, entered from that root, and never two roots in one
session.

Dot-sourced, because a script that sets $env: in its own process changes nothing for the caller.

.EXAMPLE
. .\.devbuddy\enter.ps1
#>
[CmdletBinding()]
param(
    [string] $ConfigPath,
    [string] $EnvPath
)

$ErrorActionPreference = 'Stop'

# Not $PSScriptRoot. Dot-sourcing runs the script in the caller's scope, and at an interactive
# prompt that scope has no script root at all, so every path built from it comes out empty. The
# invocation record carries this file's own path either way.
$devbuddyHome = Split-Path -Path $MyInvocation.MyCommand.Path -Parent

if ([string]::IsNullOrWhiteSpace($ConfigPath))
{
    $ConfigPath = Join-Path $devbuddyHome 'project.json'
}

if ([string]::IsNullOrWhiteSpace($EnvPath))
{
    $EnvPath = Join-Path $devbuddyHome 'deployment.env'
}

. (Join-Path $devbuddyHome 'common.ps1')

$project = Read-DevBuddyProject -Path $ConfigPath
$envValues = Read-DevBuddyEnvFile -Path $EnvPath

$deployment = Assert-DevBuddyDeploymentMatches -Project $project -EnvValues $envValues
$projectId = Assert-DevBuddyGuid -Value $project.projectId -Field 'projectId'

if ($env:DEVBUDDY_DEPLOYMENT -and $env:DEVBUDDY_DEPLOYMENT -cne $deployment)
{
    throw ("This session is already entered for deployment '$env:DEVBUDDY_DEPLOYMENT'. " +
           "Open a new shell for '$deployment' rather than loading both into one.")
}

foreach ($name in $envValues.Keys)
{
    Set-Item -Path ("Env:" + $name) -Value $envValues[$name]
}

# Computed rather than read, so it cannot drift out of step with what setup.ps1 built.
$analysisRoot = Join-Path $devbuddyHome 'projects'

$env:DEVBUDDY_ANALYSIS_ROOT_PATH = $analysisRoot

# Every setting under its framework-shaped name as well.
#
# The plugin configurations expand the short names into these, so a session entered for the
# assistant is already carrying them. The console is not launched through a plugin configuration
# and reads these names directly, so without this `dotnet run -- retention` from an entered session
# would report no connection string configured while the assistant beside it worked.
$env:DEVBUDDY_ConnectionStrings__DevBuddy = $env:DEVBUDDY_CONNECTION_STRING
$env:DEVBUDDY_Identity__SigningKey = $env:DEVBUDDY_SIGNING_KEY
$env:DEVBUDDY_Analysis__RootPath = $analysisRoot

$projectDir = Join-Path $analysisRoot $projectId

if (-not (Test-Path -LiteralPath $projectDir))
{
    Write-Warning "No junction tree at $projectDir. Run .\.devbuddy\setup.ps1 first, or every analyze_* call will report nothing to analyse."
}

if ([string]::IsNullOrWhiteSpace($env:DEVBUDDY_TOKEN))
{
    Write-Warning "DEVBUDDY_TOKEN is empty. The server will start and list its tools, and every call will be refused for lack of an identity."
}

$count = @($project.repositories).Count
$noun = if ($count -eq 1) { 'repository' } else { 'repositories' }

# The token is deliberately not printed, here or anywhere else.
Write-Host "DevBuddy: $deployment, project $projectId, $count $noun."
