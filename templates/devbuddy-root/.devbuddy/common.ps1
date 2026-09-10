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

<#
.SYNOPSIS
Refuses to continue unless deployment.env and project.json name the same workspace.

.DESCRIPTION
A machine token works in exactly one DevBuddy workspace, so deployment.env holds the token for one
workspace and project.json names the workspace its identifiers belong to. The two disagreeing
means the credential and the identifiers came from different places.

The server would refuse anyway — that is the point of scoping the token — but it would refuse with
"the credential presented is not valid in this workspace" partway through somebody's work, on a
call they had no reason to expect to fail. Catching it at the door says which two files disagree.

The workspace identifier is not a secret. It is written into deployment.env rather than read from
project.json alone so that the file carrying the credential also states what the credential is
for, which is what makes a deployment.env copied in from another root fail loudly.
#>
function Assert-DevBuddyWorkspaceMatches
{
    param(
        [Parameter(Mandatory)] $Project,
        [Parameter(Mandatory)] [hashtable] $EnvValues
    )

    $declared = Assert-DevBuddyGuid -Value $Project.workspaceId -Field 'workspaceId'
    $configured = $EnvValues['DEVBUDDY_WORKSPACE_ID']

    if ([string]::IsNullOrWhiteSpace($configured))
    {
        throw ("deployment.env has no DEVBUDDY_WORKSPACE_ID. A machine token works in one " +
               "workspace only, so the file holding the token has to say which one.")
    }

    $configured = Assert-DevBuddyGuid -Value $configured -Field 'DEVBUDDY_WORKSPACE_ID'

    if ($declared -cne $configured)
    {
        throw ("This root's token is for workspace '$configured', but project.json names " +
               "'$declared'. Mint a token in the right workspace, or fix project.json; do not " +
               "proceed until you know which of the two is wrong.")
    }

    return $declared
}

<#
.SYNOPSIS
Refuses to continue if a DevBuddy setting is stored as a persistent Windows environment variable.

.DESCRIPTION
A token set at User or Machine scope is inherited by every process that account starts, forever:
every shell, every editor, every assistant, in every folder. That is exactly the arrangement
per-root settings exist to replace, and it is invisible once set — a person would enter one root,
see their assistant work, and never learn it was working from the account-wide value instead.

Refused rather than warned, because a stale one silently wins over anything this script sets in
the current session only if the ordering is wrong, and nobody should have to reason about that.
Remove it with: [Environment]::SetEnvironmentVariable('DEVBUDDY_TOKEN', $null, 'User')
#>
function Assert-NoPersistentDevBuddySettings
{
    $names = @(
        'DEVBUDDY_TOKEN',
        'DEVBUDDY_CONNECTION_STRING',
        'DEVBUDDY_ConnectionStrings__DevBuddy',
        'DEVBUDDY_SIGNING_KEY',
        'DEVBUDDY_Identity__SigningKey',
        'DEVBUDDY_WORKSPACE_ID'
    )

    foreach ($scope in @('User', 'Machine'))
    {
        foreach ($name in $names)
        {
            $persisted = $null

            try
            {
                $persisted = [Environment]::GetEnvironmentVariable($name, $scope)
            }
            catch
            {
                # Reading Machine scope can be refused on a locked-down account. Nothing to check
                # then, and refusing to enter a root over it would help nobody.
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($persisted))
            {
                throw ("$name is set as a persistent $scope environment variable. Every process " +
                       "this account starts inherits it, in every folder, which is what these " +
                       "per-root settings exist to avoid. Remove it and re-enter this root: " +
                       "[Environment]::SetEnvironmentVariable('$name', `$null, '$scope')")
            }
        }
    }
}
