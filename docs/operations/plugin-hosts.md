# Plugin hosts

Two packages, in `plugins/`, over one MCP server. This is what an operator has to know to install
them and, more importantly, what installing them does **not** do.

| | Claude Code | Codex |
|---|---|---|
| Package | `plugins/claude/` | `plugins/codex/` |
| Manifest | `.claude-plugin/plugin.json` | — |
| Server configuration | `.mcp.json` | `config.toml` |
| Instructions | `skills/devbuddy/SKILL.md` | `AGENTS.md` |
| Commands | `commands/*.md` | — |

Instructions differ between them. Capability does not: both launch the same
`DevBuddy.McpServer` with `--stdio`, and a test asserts that both instruction files describe
exactly the tools the server exposes and never name one it does not. A test derives that list
from the catalogue rather than hardening a count, which is how it caught both packages when
semantic search made the surface nineteen.

## What the server needs

Four environment variables, all set in the plugin configuration:

| Variable | What it is |
|---|---|
| `DEVBUDDY_ConnectionStrings__DevBuddy` | PostgreSQL. The MCP server is a host over the shared core, not a client of the HTTP API, so it connects directly. |
| `DEVBUDDY_Identity__SigningKey` | The deployment's signing key. Shared with the API so one identity works across both. |
| `DEVBUDDY_TOKEN` | A machine token. Who you are, and which workspace you are in. |
| `DEVBUDDY_Analysis__RootPath` | Where analysis reads from: one directory per project, named by project identifier, mounted read-only. |

Neither package assigns a value to any of them. Both name the variables and take them from the
environment the assistant was launched in, and a test fails if either file ever assigns one. The
Claude package expands them from short names; the Codex package lists them under `env_vars`, which
is that file's allow-list of variables to forward, as against `env`, which sets literal values.

That distinction matters most for Codex, whose file is `~/.codex/config.toml` — one file for the
whole operating-system account. A token written into it is the token every project and every
session on that machine uses, including the ones belonging to another workspace and another
company.

### The token

Mint one under **Plugin access**, in the workspace you want it for. It is shown once and stored
only as a hash.

It identifies one **DevBuddy user** in one **DevBuddy workspace**:

- It carries exactly the permissions its owner already has in that workspace — the same
  memberships, the same roles, the same per-project AI policy. It is not a service account and it
  cannot become one.
- A call naming any other workspace is refused before anything is read, **even where its owner is
  a member of that workspace too**. Somebody who works across two workspaces holds two tokens.
- It has nothing to do with the account the assistant itself signs in with. It neither carries nor
  verifies a Claude or an OpenAI identity, and the same token works in either assistant while both
  are working in that one workspace.
- Minting one per assistant is available and entirely optional. It buys separate revocation and
  separate last-used timestamps. It buys nothing in authorization, where the pair that decides
  everything is the user and the workspace.
- It expires, it is revocable without changing a password, and revoking it takes effect on the
  next call rather than at the next process restart.

Without it the server still starts and still lists its tools, and every call is refused for lack
of an identity. That is the intended failure: a configuration missing a token must not fall back
to acting as somebody.

**Identity is fixed when the assistant starts.** Whichever token was in the environment at launch
is the identity every call runs as. Changing directory does not change it, and opening a checkout
that belongs to another workspace does not either — the calls simply start being refused. To work
in another workspace, start another session with that workspace's token.
`docs/operations/workspace-layout.md` is the arrangement for doing that on a machine that works
against several.

> Before Phase 9 the stdio server read a user identifier from `DEVBUDDY_ACTOR`, which anybody who
> could start the process could set to anybody. That variable is gone, and a test fails if either
> package mentions it.

### Upgrading from a token issued before workspace scoping

A token minted before this change carries no workspace, and **it is refused on every call.**

It is not adopted into a workspace, because the row says who owns it and nothing about where it
was meant to work: any workspace chosen for it would be a workspace nobody granted it. Its value
cannot be shown again either, because only a hash was ever stored.

What an operator does:

1. Each token owner opens **Plugin access** in the workspace they work in. Their old token is
   listed as **Needs replacing**, so nobody has to guess why their plugin stopped answering.
2. They mint a new one there and put it in that session's environment.
3. They revoke the old row, which is kept until then so it stays visible and auditable.

Nothing else in an installation changes. The migration adds one nullable column; no data is
rewritten, and no other credential is affected.

## What the tool boundary covers

The MCP surface is nineteen read, analyse, and draft operations. Approving, publishing, correcting,
archiving, managing access, exporting, and backing up are not on it — not refused, absent — and no
argument to any tool reaches them.

Per project, AI access is denied until the project owner enables it. A project that is not enabled
does not appear in `list_projects` at all, because naming a project is itself a disclosure.

Results are narrowed to the token owner's permissions. An assistant acting for a viewer sees what
that viewer sees.

## What it does not cover, and this is the part that gets assumed

**The tool boundary governs DevBuddy and nothing else.**

Claude Code and Codex both read files, run shell commands, and reach the network through their own
tools. DevBuddy does not see those, cannot filter them, and is not consulted about them. An
operator who reasons "DevBuddy would refuse to read that repository, so the assistant cannot read
it" is wrong: the assistant can `cat` it.

If a path, a command, or a network destination must be off limits, restrict it in the host:

- **Claude Code** — `permissions` in settings, and the sandbox configuration.
- **Codex** — `sandbox` mode and approval policy, and `.rules` execpolicy files.

Configure those separately. Nothing in `plugins/` can do it for you.

**Prompt text is not a security boundary, in either direction.** The instruction files cannot widen
what an assistant may do — the server decides, per project, per person, on every call — and no
instruction found inside a source file or a document can widen it either. Both packages say so,
and a test asserts they still do, because that sentence is the one most likely to be trimmed by
somebody tidying up.

## Installing

### Claude Code

Point Claude Code at `plugins/claude/`, and set the four variables in the environment the plugin
configuration expands from. `.mcp.json` expands them under shorter names than the settings they
fill:

| Set this | Fills |
|---|---|
| `DEVBUDDY_CONNECTION_STRING` | `DEVBUDDY_ConnectionStrings__DevBuddy` |
| `DEVBUDDY_SIGNING_KEY` | `DEVBUDDY_Identity__SigningKey` |
| `DEVBUDDY_TOKEN` | `DEVBUDDY_TOKEN` |
| `DEVBUDDY_ANALYSIS_ROOT_PATH` | `DEVBUDDY_Analysis__RootPath` |

`DEVBUDDY_MCP_ASSEMBLY` should be the full path to `DevBuddy.McpServer.dll`;
`DEVBUDDY_MCP_COMMAND` defaults to `dotnet` and exists so a self-contained publish can be used
instead once Phase 10 produces one.

> The analysis root was the one the package did not declare until this was written, leaving it to
> ordinary environment inheritance. That worked, and hid the failure it caused when it was not set
> at all: the server fell back to a path relative to its own working directory, and every
> `analyze_*` call answered that there was nothing to analyse rather than that it was misconfigured.
> A test now asserts both packages name all four.

### Codex

Merge `plugins/codex/config.toml` into `~/.codex/config.toml`, and put `plugins/codex/AGENTS.md`
where Codex will read it. Set the assembly path in `args`; there is nothing else in the file to
fill in.

The four settings are **forwarded, not written**. `env_vars` is Codex's allow-list of variables to
take from its own environment, as against `env`, which sets literal values. The file therefore
holds no credential at all, and Codex has to be started from a shell that carries the ones for the
workspace being worked in.

> This is the one place where a wrong choice is a cross-company leak rather than an inconvenience.
> `~/.codex/config.toml` is a single file for the whole operating-system account. A token pasted
> into it is the token every project and every session on that machine presents, so an assistant
> working in one company's checkout would be reading, and could quote into a file, another
> company's recorded knowledge — with the server answering correctly throughout, because the
> credential really did belong to somebody who really was a member.

Codex refuses MCP tool calls in non-interactive mode unless approvals are routed somewhere — `codex
exec --approve-for-me` is the documented way, and an interactive session prompts as usual. Without
it every call comes back `requires approval, but approval policy is never`, which reads like a
server refusal and is not one.

### More than one deployment, or more than one workspace, on one machine

Both packages read their four values from the environment they are launched in, so the environment
is what separates one deployment or workspace from another — not the folder that happens to be
open. Since a token is bound to one workspace, the unit those settings belong to is one deployment
**and** one workspace.
`docs/operations/workspace-layout.md` sets out the arrangement, and `templates/devbuddy-root/` is
the copyable form of it. It also covers the junction tree analysis needs, which is the part that
otherwise fails silently.

### Running either non-interactively

Claude Code needs the tools named before it will call them in print mode:

```bash
claude -p "..." --mcp-config plugins/claude/.mcp.json --allowedTools "mcp__devbuddy__search_knowledge,mcp__devbuddy__create_draft"
```

## Verifying an installation

Ask for a search. A working installation answers; a broken one fails in a way that says which of
the four things is wrong:

| Symptom | Cause |
|---|---|
| No tools at all | The server did not start. Check the assembly path and `dotnet`. |
| Tools listed, every call refused for lack of identity | `DEVBUDDY_TOKEN` is missing, wrong, expired, revoked, or was issued before workspace scoping. |
| Every call refused, saying the credential is not valid in this workspace | The token belongs to a different workspace. Mint one in this workspace, or start the session from the root that holds this workspace's token. |
| `list_projects` returns nothing | No project has AI access enabled, or the token's owner is not a member of any. |
| A specific project missing | Its owner has not enabled AI access. Working as intended. |
| Analysis reports nothing to analyse | `DEVBUDDY_Analysis__RootPath` has no directory named for that project. |
