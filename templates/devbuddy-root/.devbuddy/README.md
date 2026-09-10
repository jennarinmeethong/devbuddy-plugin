# .devbuddy

Settings for one DevBuddy deployment and one DevBuddy workspace, for every repository under this
root.

It sits **beside** the repositories, never inside one. That is the whole point: nothing here is
committed, nothing travels with `git clone`, and no pull request can edit it.

```
root\
  git1\  git2\  git3\      the checkouts, keeping their own names
  .devbuddy\
    project.json           which project and workspace these repositories are, and which deployment
    deployment.env         the workspace, the connection string, and your machine token
    projects\              junctions, built by setup.ps1
      <projectId>\
        <repositoryId>  -> ..\..\..\git1
```

## One project, three repositories

The three checkouts are one project holding three source repositories, not three projects. Work
items and knowledge records hang off a project, so a change that touches two of these checkouts
stays one piece of work.

## Getting started

1. `copy .devbuddy\deployment.env.example .devbuddy\deployment.env` and fill it in. Mint the
   machine token under **Plugin access** in the workspace this root is for; it works in that
   workspace and no other. Put that workspace's identifier in `DEVBUDDY_WORKSPACE_ID` too, so
   `enter.ps1` can tell you at once if the two files came from different places.
2. Put the real `workspaceId` and `projectId` in `project.json`, from `list_projects` or the web
   interface. Lowercase, hyphenated, no braces.
3. Pick a `repositoryId` per checkout. You choose these: nothing persists a `SourceRepository`
   today, so they are a naming convention and no more. Keep them stable once chosen.
4. `.\.devbuddy\setup.ps1`
5. In the session you launch the assistant from: `. .\.devbuddy\enter.ps1`

## What this is not

It is a convenience, not a control. Nothing in DevBuddy reads these files. What any call may reach
is decided by the server, per project, per person, on every call, from the permissions the machine
token's owner already has. Editing `project.json` changes which project gets asked about; it does
not change what the answer is allowed to be.

Three rules that carry the real weight:

- **One session per root.** The identity is whatever was in the environment when the assistant was
  launched. Changing directory afterwards changes nothing, and neither does opening a checkout
  that belongs to another workspace — the calls simply start being refused.
- **Never set these as persistent Windows environment variables.** At User or Machine scope they
  are inherited by every process this account starts, in every folder, which is the arrangement
  this folder exists to replace. `enter.ps1` refuses to run if it finds one.
- **Keep this root off any sync.** `deployment.env` holds a credential in plain text.

One root per deployment **and** workspace. A machine token works in exactly one DevBuddy
workspace, so two workspaces mean two roots, two tokens, and two sessions. The same token works in
Claude and in Codex; it identifies you to DevBuddy and has nothing to do with an account with
either of them.

## Where the reasoning is written down

`docs/operations/workspace-layout.md` in the DevBuddy repository, which covers why the junction
tree exists, what the guards catch, and what this arrangement deliberately does not protect.
