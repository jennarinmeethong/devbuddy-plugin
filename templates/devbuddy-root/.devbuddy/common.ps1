# Shared by setup.ps1 and enter.ps1. Dot-sourced, never run on its own.

function Read-DevBuddyEnvFile
{
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path))
    {
        throw "No environment file at $Path. Copy deployment.env.example to deployment.env and fill it in."
    }

    $values = @{}

    foreach ($line in (Get-Content -LiteralPath $Path -Encoding UTF8))
    {
        $trimmed = $line.Trim()

        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#'))
        {
            continue
        }

        $split = $trimmed.IndexOf('=')

        if ($split -lt 1)
        {
            continue
        }

        $name = $trimmed.Substring(0, $split).Trim()

        # Not trimmed on the right beyond whitespace: a password may legitimately contain '=' and
        # anything else, so everything after the first '=' is the value, verbatim.
        $values[$name] = $trimmed.Substring($split + 1).Trim()
    }

    return $values
}

function Read-DevBuddyProject
{
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path))
    {
        throw "No project file at $Path."
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

<#
.SYNOPSIS
Returns the identifier only if it is a GUID in the exact form Guid.ToString() produces.

.DESCRIPTION
AnalysisOptions.RootFor joins the identifier onto the path verbatim, with no normalisation, so
"3C7E..." in braces or uppercase names a directory the server will never look in. Catching it here
turns a silent "nothing to analyse" into a message that says which field is wrong.
#>
function Assert-DevBuddyGuid
{
    param(
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Value,
        [Parameter(Mandatory)] [string] $Field
    )

    if ([string]::IsNullOrWhiteSpace($Value))
    {
        throw "$Field is missing from project.json."
    }

    $parsed = [Guid]::Empty

    if (-not [Guid]::TryParse($Value, [ref] $parsed))
    {
        throw "$Field is not a GUID: '$Value'."
    }

    if ($parsed -eq [Guid]::Empty)
    {
        throw "$Field is still the all-zero placeholder. Take the real value from list_projects or the web interface."
    }

    if ($parsed.ToString() -cne $Value)
    {
        throw "$Field must be lowercase and hyphenated, with no braces: expected '$($parsed.ToString())', found '$Value'."
    }

    return $parsed.ToString()
}

<#
.SYNOPSIS
Refuses to continue unless deployment.env and project.json name the same deployment.

.DESCRIPTION
The one check that makes a project.json copied in from another company's root fail immediately and
legibly, rather than resolving its GUIDs against whichever token happens to be loaded.
#>
function Assert-DevBuddyDeploymentMatches
{
    param(
        [Parameter(Mandatory)] $Project,
        [Parameter(Mandatory)] [hashtable] $EnvValues
    )

    $declared = $Project.deployment
    $configured = $EnvValues['DEVBUDDY_DEPLOYMENT']

    if ([string]::IsNullOrWhiteSpace($declared))
    {
        throw "project.json has no 'deployment' label."
    }

    if ([string]::IsNullOrWhiteSpace($configured))
    {
        throw "deployment.env has no DEVBUDDY_DEPLOYMENT."
    }

    if ($declared -cne $configured)
    {
        throw ("This root is configured for deployment '$configured', but project.json belongs to " +
               "'$declared'. One of the two files came from somewhere else; do not proceed until you know which.")
    }

    return $declared
}
