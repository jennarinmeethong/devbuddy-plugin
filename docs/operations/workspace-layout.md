# Workspace layout

How to arrange checkouts on a machine that works against more than one DevBuddy deployment, or
more than one workspace in one deployment.

This is a convention for the operator's side of the plugin, not a product feature. Nothing in
`src/` reads any of it. `templates/devbuddy-root/` is the copyable form.

## The problem it solves

A DevBuddy installation is self-hosted, so a person can plausibly hold a machine token for one
company's deployment and a token for another's on the same laptop. Both tokens work. Both sets of
tools are callable. Nothing on the server can tell that one of them should not be in play right
now, because the server only ever sees one call at a time from one credential and answers it
correctly.

What goes wrong is not that data crosses between deployments — it cannot, they are separate
databases, and inside one deployment tenant isolation is enforced on every request. What goes
wrong is that an assistant reads one company's recorded knowledge while working in another
company's checkout, and carries it across by writing it into a draft or a source file. That
happens in the host, not in DevBuddy, and it is the same gap `plugin-hosts.md` describes: the tool
boundary governs DevBuddy and nothing else.

The only place that gap closes is before launch. One deployment's credentials in the environment,
and no other's.

## One root per deployment *and* workspace

The original arrangement was one root per deployment. That was the right unit while a machine
token was bound only to its owner, because the deployment was the only thing the credential
distinguished: one token reached every workspace its owner belonged to inside it.

A token is now bound to one **workspace** as well, so the unit is one root per deployment and
workspace. Two workspaces in one deployment mean two roots, two tokens, and two sessions.

This is the same failure the deployment boundary already had, one level in. Two workspaces in one
installation are usually two clients, or a client and an internal system: separate knowledge,
separate people, and no reason for one session to hold both. Before the token carried a workspace,
a single credential covered all of them and nothing in the environment could narrow it. Now the
server refuses the mismatch, and the layout below is what stops that refusal arriving as a
surprise partway through somebody's work.

Two things the layout adds for it:

- `deployment.env` states `DEVBUDDY_WORKSPACE_ID`, and `enter.ps1` refuses to load it if it
  disagrees with `workspaceId` in `project.json`. That is what catches a `deployment.env` copied
  in from another root: the credential and the identifiers came from different places, and the
  server would otherwise be the first thing to notice.
- Entering a second workspace into a session already entered for one is refused, exactly as a
  second deployment already was.

## The layout

```
root\
  git1\  git2\  git3\        the checkouts, keeping their own names
  .gitignore                 insurance, in case anyone ever runs `git init` here
  .devbuddy\
    README.md                what somebody reads when they find this folder
    project.json             which project and workspace these checkouts are, and which deployment
    deployment.env           workspace, connection string, signing key, machine token
    common.ps1               shared by the two scripts below
    setup.ps1                builds the junction tree, once and after every change
    enter.ps1                dot-sourced, loads this root into the current session
    projects\                built by setup.ps1, not written by hand
      <projectId>\
        <repositoryId>  ->  ..\..\..\git1
```

One root per deployment and workspace. Everything under it belongs to that pair and to no other.

**`.devbuddy` sits beside the checkouts, never inside one.** That is the whole of the design. A
settings file inside a repository is committed, travels with `git clone`, gets copied along with a
template, and can be edited by a pull request. A repository under study is content somebody else
wrote — that assumption is why `UrlGuard` exists and why both plugin packages say that what a tool
returns is data rather than instruction. A file that selects which credential is used has no
business being subject to it. Moving it up one level removes all four problems at once.

## Why there is a junction tree

`AnalysisOptions.RootFor` resolves a scope to a path by joining identifiers verbatim:

```
<Analysis:RootPath>\<projectId>\<repositoryId>
```

There is no normalisation and no lookup. A directory named `git1` is not a directory analysis will
ever look in, and the failure is silent: `IsAnalysable` returns false and every `analyze_*` call
reports that there is nothing to analyse.

Renaming the checkouts to GUIDs would satisfy it and make the folder unusable for people.
`setup.ps1` builds the shape the server wants out of junctions instead, so the checkouts keep
their names and analysis still finds them.

Identifiers must be in the exact form `Guid.ToString()` produces: lowercase, hyphenated, no
braces. `setup.ps1` refuses anything else rather than creating a directory the server will never
open.

### One project, three repositories

The template models the three checkouts as **one project holding three source repositories**, not
as three projects. Work items and knowledge records hang off a project, so a change that touches
two checkouts stays one piece of work with one set of recorded decisions.

If the checkouts are genuinely unrelated products, make them separate projects instead. Then
`repositoryId` is not needed at all — `RootFor` stops at the project level when no repository is
named, and the junction tree is one level shallower.

`repositoryId` is yours to choose. Nothing in the system persists a `SourceRepository` today, so it
is a naming convention and no more, the same convention `GitHubOptions.Repositories` already uses.
`workspaceId` and `projectId` are not: take them from `list_projects` or from the web interface.

## The settings, and how they arrive

`deployment.env` holds the values the plugin configuration expands from. `enter.ps1` loads them
into the current session, and the server inherits them when the host launches it.

| Variable | Set in | Notes |
|---|---|---|
| `DEVBUDDY_CONNECTION_STRING` | `deployment.env` | Expanded into `DEVBUDDY_ConnectionStrings__DevBuddy` by the Claude package. |
| `DEVBUDDY_SIGNING_KEY` | `deployment.env` | Expanded into `DEVBUDDY_Identity__SigningKey`. At least 32 characters, or the host refuses to start. |
| `DEVBUDDY_WORKSPACE_ID` | `deployment.env` | The workspace the token below belongs to. Not a secret, and not read by the server: it is here so the file holding the credential says what the credential is for, and so `enter.ps1` can refuse a pair that disagrees. |
| `DEVBUDDY_TOKEN` | `deployment.env` | Your machine token, for that workspace. Without it the server starts, lists its tools, and refuses every call. |
| `DEVBUDDY_ANALYSIS_ROOT_PATH` | computed by `enter.ps1` | Not read from the file, so it cannot drift out of step with the junctions `setup.ps1` built. |

### Never as a persistent environment variable

`enter.ps1` refuses to run if any of these is set at Windows **User** or **Machine** scope, and it
is worth being blunt about why. A variable set there is inherited by every process the account
starts, for ever, in every folder — which is precisely the account-wide arrangement these per-root
settings exist to replace, and it is invisible once done. Somebody would enter a root, watch their
assistant work, and never learn it had been working from the account-wide token all along.

Remove one with:

```powershell
[Environment]::SetEnvironmentVariable('DEVBUDDY_TOKEN', $null, 'User')
```

`enter.ps1` also sets every one of them under its framework-shaped name —
`DEVBUDDY_ConnectionStrings__DevBuddy`, `DEVBUDDY_Identity__SigningKey`,
`DEVBUDDY_Analysis__RootPath`. The plugin configurations produce those names by expanding the
short ones, so the assistant does not need it; the console does. It is not launched through a
plugin configuration and reads the framework-shaped names directly, so without this
`dotnet run -- retention` from an entered session would report no connection string configured
while the assistant beside it worked.

## What the scripts refuse

Each of these is a mistake that would otherwise fail silently, or fail as a confusing `NotFound`.

| Situation | What happens |
|---|---|
| `deployment.env` and `project.json` name different deployments | Refused. This is a `project.json` that came from another root; the GUIDs in it mean nothing here. |
| `deployment.env` and `project.json` name different workspaces | Refused. The credential and the identifiers came from different places, and the token would be refused by the server on the first call anyway. |
| A DevBuddy setting is a persistent User or Machine environment variable | Refused, naming the variable and the command that removes it. |
| An identifier is not a canonical lowercase GUID | Refused, naming the field and the form expected. |
| An identifier is still the all-zero placeholder | Refused. The template ships unfilled on purpose. |
| A checkout named in `project.json` is missing | Refused before anything is created, so a typo cannot leave the tree half built. |
| A checkout has no `.git` | Warned. Analysis reads git references from working-copy metadata, so history will be reported as unavailable. |
| `enter.ps1` run in a session already entered for another deployment | Refused. Open a new shell instead. |
| `enter.ps1` run in a session already entered for another workspace | Refused, for the same reason: the last one loaded would silently win. |
| A path where a junction belongs is a real directory | Refused, left untouched. |

Removing a stale link deletes the reparse point only, through `Directory.Delete` with recursion
off. `Remove-Item -Recurse` on a junction has historically deleted through the link, which here
would mean deleting the checkout it points at.

## What this does not do

**It is not a control.** No operation reads `project.json`, and editing it changes only which
project gets asked about. What any call may reach is still decided by the server, per project, per
person, on every call, from the permissions the token owner already has, in the one workspace the
token was minted in. A `project.json` pointed at a project the token cannot see produces a
refusal, not a leak — but the refusal is the server's doing, not this file's.

The workspace check `enter.ps1` performs is the same kind of thing. It catches a mismatched pair
at the door, where the message can say which two files disagree; the server would have refused the
call regardless, and that refusal is what actually holds.

**The isolation boundary is the process, not the directory.** The identity in force is whatever
was in the environment when the assistant was launched. Changing directory afterwards changes
nothing, and neither does opening a different checkout. One session per root, entered from that
root.

**`deployment.env` is a credential in plain text on disk.** ADR-0006 accepts that for stdio: the
token carries only permissions its owner already has, in one workspace, and is revocable on the
next call. What the ADR does not cover is the file leaving the machine, so keep the root out of
OneDrive, Dropbox, and any other sync.

**Host restrictions still have to be configured separately.** If a path or a command must be off
limits, that is `permissions` and the sandbox in Claude Code, or `sandbox` mode, the approval
policy, and `.rules` in Codex. Nothing here does it for you.
