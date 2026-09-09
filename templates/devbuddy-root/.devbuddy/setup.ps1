<#
.SYNOPSIS
Builds the junction tree analysis reads from, for one project holding several repositories.

.DESCRIPTION
AnalysisOptions.RootFor(scope, repositoryId) resolves to

    <Analysis:RootPath>\<projectId>\<repositoryId>

which is not a shape anybody wants to name their working folders after. This creates that shape
under .devbuddy\projects as junctions pointing back at the repositories beside this directory, so
the checkouts keep their readable names and the server still finds them.

Run it once, and again whenever project.json changes.

.EXAMPLE
.\.devbuddy\setup.ps1
#>
[CmdletBinding()]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'project.json'),
    [string] $EnvPath = (Join-Path $PSScriptRoot 'deployment.env')
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

$project = Read-DevBuddyProject -Path $ConfigPath
$envValues = Read-DevBuddyEnvFile -Path $EnvPath

$deployment = Assert-DevBuddyDeploymentMatches -Project $project -EnvValues $envValues

$workspaceId = Assert-DevBuddyGuid -Value $project.workspaceId -Field 'workspaceId'
$projectId = Assert-DevBuddyGuid -Value $project.projectId -Field 'projectId'

if (-not $project.repositories -or $project.repositories.Count -eq 0)
{
    throw "project.json lists no repositories."
}

$rootPath = Split-Path -Path $PSScriptRoot -Parent
$projectsRoot = Join-Path $PSScriptRoot 'projects'
$projectDir = Join-Path $projectsRoot $projectId

New-Item -ItemType Directory -Path $projectDir -Force | Out-Null

# Resolve and check every repository before creating anything, so a typo in the third entry does
# not leave the tree half built.
$planned = @()
$seenIds = @{}
$seenPaths = @{}

foreach ($repository in $project.repositories)
{
    $repositoryId = Assert-DevBuddyGuid -Value $repository.repositoryId -Field "repositoryId for '$($repository.path)'"

    if ($seenIds.ContainsKey($repositoryId))
    {
        throw "repositoryId $repositoryId appears twice in project.json."
    }

    if ([string]::IsNullOrWhiteSpace($repository.path))
    {
        throw "A repositories entry has no path."
    }

    if ($seenPaths.ContainsKey($repository.path))
    {
        throw "path '$($repository.path)' appears twice in project.json."
    }

    $seenIds[$repositoryId] = $true
    $seenPaths[$repository.path] = $true

    $target = Join-Path $rootPath $repository.path

    if (-not (Test-Path -LiteralPath $target -PathType Container))
    {
        throw "'$($repository.path)' is not a directory under $rootPath."
    }

    if (-not (Test-Path -LiteralPath (Join-Path $target '.git')))
    {
        Write-Warning "$($repository.path) has no .git. Analysis reads git references from working-copy metadata, so history will be reported as unavailable."
    }

    $planned += [pscustomobject]@{
        RepositoryId = $repositoryId
        Path         = $repository.path
        Target       = (Resolve-Path -LiteralPath $target).ProviderPath
        Link         = (Join-Path $projectDir $repositoryId)
    }
}

# Remove links for repositories that project.json no longer lists. Only reparse points are removed,
# and only the reparse point itself: Remove-Item -Recurse on a junction has historically deleted
# through the link, which here would delete the checkout it points at.
foreach ($existing in (Get-ChildItem -LiteralPath $projectDir -Directory -Force -ErrorAction SilentlyContinue))
{
    if ($seenIds.ContainsKey($existing.Name))
    {
        continue
    }

    if (($existing.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne [System.IO.FileAttributes]::ReparsePoint)
    {
        Write-Warning "$($existing.FullName) is a real directory, not a junction. Leaving it alone; remove it by hand if it is stale."
        continue
    }

    [System.IO.Directory]::Delete($existing.FullName, $false)
    Write-Host "  removed stale junction $($existing.Name)"
}

foreach ($entry in $planned)
{
    if (Test-Path -LiteralPath $entry.Link)
    {
        $item = Get-Item -LiteralPath $entry.Link -Force

        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne [System.IO.FileAttributes]::ReparsePoint)
        {
            throw ("$($entry.Link) already exists and is a real directory, not a junction. " +
                   "Refusing to touch it; move it out of the way first.")
        }

        [System.IO.Directory]::Delete($entry.Link, $false)
    }

    New-Item -ItemType Junction -Path $entry.Link -Target $entry.Target | Out-Null
    Write-Host "  $($entry.RepositoryId) -> $($entry.Path)"
}

Write-Host ""
Write-Host "Deployment  $deployment"
Write-Host "Workspace   $workspaceId"
Write-Host "Project     $projectId"
Write-Host "Analysis    $projectsRoot"
Write-Host ""
Write-Host "Now dot-source enter.ps1 in the session you will launch the assistant from:"
Write-Host "  . .\.devbuddy\enter.ps1"
