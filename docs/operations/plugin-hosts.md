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
exactly the eighteen tools the server exposes and never name one it does not.

## What the server needs

Four environment variables, all set in the plugin configuration:

| Variable | What it is |
|---|---|
| `DEVBUDDY_ConnectionStrings__DevBuddy` | PostgreSQL. The MCP server is a host over the shared core, not a client of the HTTP API, so it connects directly. |
| `DEVBUDDY_Identity__SigningKey` | The deployment's signing key. Shared with the API so one identity works across both. |
| `DEVBUDDY_TOKEN` | A machine token. Who you are. |
| `DEVBUDDY_Analysis__RootPath` | Where analysis reads from: one directory per project, named by project identifier, mounted read-only. |

### The token

Mint one under **Plugin access** in the web interface, or from the console. It is shown once and
stored only as a hash.

It carries exactly the permissions its owner already has — the same memberships, the same roles,
the same per-project AI policy. It is not a service account and it cannot become one. It expires,
it is revocable without changing a password, and revoking it takes effect on the next call rather
than at the next process restart.

Without it the server still starts and still lists its tools, and every call is refused for lack
of an identity. That is the intended failure: a configuration missing a token must not fall back
to acting as somebody.

> Before Phase 9 the stdio server read a user identifier from `DEVBUDDY_ACTOR`, which anybody who
> could start the process could set to anybody. That variable is gone, and a test fails if either
> package mentions it.

## What the tool boundary covers

The MCP surface is eighteen read, analyse, and draft operations. Approving, publishing, correcting,
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
configuration expands from. `DEVBUDDY_MCP_ASSEMBLY` should be the full path to
`DevBuddy.McpServer.dll`; `DEVBUDDY_MCP_COMMAND` defaults to `dotnet` and exists so a self-contained
publish can be used instead once Phase 10 produces one.

### Codex

Merge `plugins/codex/config.toml` into `~/.codex/config.toml`, filling in the four values, and put
`plugins/codex/AGENTS.md` where Codex will read it.

Codex refuses MCP tool calls in non-interactive mode unless approvals are routed somewhere — `codex
exec --approve-for-me` is the documented way, and an interactive session prompts as usual. Without
it every call comes back `requires approval, but approval policy is never`, which reads like a
server refusal and is not one.

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
| Tools listed, every call refused for lack of identity | `DEVBUDDY_TOKEN` is missing, wrong, expired, or revoked. |
| `list_projects` returns nothing | No project has AI access enabled, or the token's owner is not a member of any. |
| A specific project missing | Its owner has not enabled AI access. Working as intended. |
| Analysis reports nothing to analyse | `DEVBUDDY_Analysis__RootPath` has no directory named for that project. |
