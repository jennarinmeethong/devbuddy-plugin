# Workspace layout

How to arrange checkouts on a machine that works against more than one DevBuddy deployment.

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

## The layout

```
root\
  git1\  git2\  git3\        the checkouts, keeping their own names
  .gitignore                 insurance, in case anyone ever runs `git init` here
  .devbuddy\
    README.md                what somebody reads when they find this folder
    project.json             which project these checkouts are, and which deployment
    deployment.env           connection string, signing key, machine token
    common.ps1               shared by the two scripts below
    setup.ps1                builds the junction tree, once and after every change
    enter.ps1                dot-sourced, loads this root into the current session
    projects\                built by setup.ps1, not written by hand
      <projectId>\
        <repositoryId>  ->  ..\..\..\git1
```

One root per deployment. Everything under it belongs to that deployment and to no other.

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

## The four settings, and how they arrive

`deployment.env` holds the values the plugin configuration expands from. `enter.ps1` loads them
into the current session, and the server inherits them when the host launches it.

| Variable | Set in | Notes |
|---|---|---|
| `DEVBUDDY_CONNECTION_STRING` | `deployment.env` | Expanded into `DEVBUDDY_ConnectionStrings__DevBuddy` by the Claude package. |
| `DEVBUDDY_SIGNING_KEY` | `deployment.env` | Expanded into `DEVBUDDY_Identity__SigningKey`. At least 32 characters, or the host refuses to start. |
| `DEVBUDDY_TOKEN` | `deployment.env` | Your machine token. Without it the server starts, lists its tools, and refuses every call. |
| `DEVBUDDY_Analysis__RootPath` | computed by `enter.ps1` | Not read from the file, so it cannot drift out of step with the junctions `setup.ps1` built. |

> The Claude package's `.mcp.json` names the first three in its `env` block and does not name
> `DEVBUDDY_Analysis__RootPath`; the Codex `config.toml` names all four. It reaches the server by
> ordinary environment inheritance from the session `enter.ps1` was dot-sourced in, which is why
> this arrangement works today. Setting it in the shell is not optional on the Claude side — it is
> the only route it has.

## What the scripts refuse

Each of these is a mistake that would otherwise fail silently, or fail as a confusing `NotFound`.

| Situation | What happens |
|---|---|
| `deployment.env` and `project.json` name different deployments | Refused. This is a `project.json` that came from another root; the GUIDs in it mean nothing here. |
| An identifier is not a canonical lowercase GUID | Refused, naming the field and the form expected. |
| An identifier is still the all-zero placeholder | Refused. The template ships unfilled on purpose. |
| A checkout named in `project.json` is missing | Refused before anything is created, so a typo cannot leave the tree half built. |
| A checkout has no `.git` | Warned. Analysis reads git references from working-copy metadata, so history will be reported as unavailable. |
| `enter.ps1` run in a session already entered for another deployment | Refused. Open a new shell instead. |
| A path where a junction belongs is a real directory | Refused, left untouched. |

Removing a stale link deletes the reparse point only, through `Directory.Delete` with recursion
off. `Remove-Item -Recurse` on a junction has historically deleted through the link, which here
would mean deleting the checkout it points at.

## What this does not do

**It is not a control.** No operation reads `project.json`, and editing it changes only which
project gets asked about. What any call may reach is still decided by the server, per project, per
person, on every call, from the permissions the token owner already has. A `project.json` pointed
at a project the token cannot see produces a refusal, not a leak — but the refusal is the server's
doing, not this file's.

**The isolation boundary is the process, not the directory.** The identity in force is whatever
was in the environment when the assistant was launched. Changing directory afterwards changes
nothing, and neither does opening a different checkout. One session per root, entered from that
root.

**`deployment.env` is a credential in plain text on disk.** ADR-0006 accepts that for stdio: the
token carries only permissions its owner already has and is revocable on the next call. What the
ADR does not cover is the file leaving the machine, so keep the root out of OneDrive, Dropbox, and
any other sync.

**Host restrictions still have to be configured separately.** If a path or a command must be off
limits, that is `permissions` and the sandbox in Claude Code, or `sandbox` mode, the approval
policy, and `.rules` in Codex. Nothing here does it for you.
