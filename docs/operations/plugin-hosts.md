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

Instructions differ between them. Capability does not: both reach the same MCP server, and a test
asserts that both instruction files describe exactly the tools the server exposes and never name
one it does not. A test derives that list from the catalogue rather than hardening a count, which
is how it caught both packages when semantic search and the embedding sweep took the surface to
twenty.

## How a plugin reaches the server (Phase 14, A4)

**Over HTTPS, at `/mcp`, with the person's own machine token** (`info.md`, 2026-09-27; ADR-0006's
amendment and ADR-0015). The address is the one the web interface is on; the gateway passes `/mcp`
to the MCP service and everything else to the API.

Neither package holds a token or any server secret. The token is kept by the **`devbuddy` client**,
in the operating system's credential store, and handed to the connection by its header helper,
`devbuddy mcp-headers`, which both assistants run when they connect:

| | Claude Code | Codex |
|---|---|---|
| Setting | `headersHelper` in `.mcp.json` | `http_headers_helper` in `config.toml` |
| Where it runs | the plugin's folder, so the package passes `${CLAUDE_PROJECT_DIR}` | the session's folder (to be confirmed on the Mac mini) |
| The URL it checks | `CLAUDE_CODE_MCP_SERVER_URL`, set by Claude Code | `--url`, written beside `url` in the file |

The helper finds the registered checkout that holds that folder, and prints the `Authorization`
header only if the URL is on the server that checkout is registered to. An unregistered folder, a
missing token, or another server gets nothing, and the assistant's calls are refused.

Until A4 both packages started `DevBuddy.McpServer --stdio` locally, with the database connection
string and the signing key in the person's environment. That route still exists for an
administrator, below, and is not what a person installs.

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

The MCP surface is twenty read, analyse, and draft operations. Approving, publishing, correcting,
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

### The `devbuddy` client, once per machine

Each release archive carries it in `Client/`: `devbuddy` on Linux and macOS, `devbuddy.exe` on
Windows. Put that folder on `PATH`. It is unsigned (B2): on Windows with Smart App Control on it
cannot run at all, and there is no per-program exception.

Trust the gateway's certificate on the machine too, the root CA the web interface already needs.
Codex reads a CA from `CODEX_CA_CERTIFICATE` or `SSL_CERT_FILE`.

### Each checkout

```bash
devbuddy register --server https://192.168.1.160:5010 --workspace <workspace id> [--project <project id>]
```

Run it in the checkout, or pass its path. It asks for a machine token the first time a workspace is
used (mint one under **Plugin access**), with the input hidden, checks it against the server, and
stores it. A second checkout in the same workspace is not asked again. `devbuddy doctor` checks the
whole path: registration, token, certificate, and that the server accepts the token in the
workspace. `list`, `show`, `update`, `unregister`, `token set` and `token remove` manage the rest.

### Claude Code

Point Claude Code at `plugins/claude/`, and set `DEVBUDDY_URL` to the server's address, without
`/mcp`, in the environment it starts with, for instance `env` in `~/.claude/settings.json`.
`DEVBUDDY_CLIENT` names the client if it is not on `PATH`.

### Cowork

Cowork does not run a header helper from a plugin, so `plugins/cowork/` starts the client's
bridge instead: `devbuddy mcp-bridge`, a local MCP server over stdio that Cowork runs on the
machine itself, outside its sandbox, where the credential store and the LAN are. It needs no
repository and no code.

1. Put the client on `PATH` and trust the gateway's certificate, as above. Cowork starts the
   bridge by name, so a client that is not on `PATH` is not found.
2. Register once, in any folder that is not a drive root or the home folder, for instance a folder
   of documents: `devbuddy register --server https://192.168.1.160:5010 --workspace <workspace id>`.
   A folder need not be a git repository.
3. Zip the contents of `plugins/cowork/` and upload the zip under Plugins in Cowork. Cowork warns
   that the plugin runs a local process; that process is the bridge.
4. Start a Cowork task **on this machine** and ask what projects DevBuddy has. A Cowork project
   whose threads run away from this machine cannot start the bridge.

Cowork starts the bridge in the system folder and tells it nothing about the folder the task works
in: not in the working directory, not in a variable, and not through MCP roots, which it does not
offer. So the bridge cannot choose a registration by folder.

- **One workspace registered on the machine:** the bridge uses it, and nothing is asked.
- **More than one, which is how customers are kept apart** (`info.md`, 2026-09-29): every tool
  call is held and answered that a workspace has to be chosen, and the tools include
  `use_workspace`. The assistant asks the person which workspace the task is for and calls it. The
  choice holds until the task ends, and a second choice of another workspace is refused, so one
  task reads one customer's knowledge. The bridge is started per task, so a new task chooses again.
  Give each workspace a name when registering it, `devbuddy register --label "Customer A"` (or
  `devbuddy update --label` later); without one the choices are named by their registered folders.
- **Nothing registered, or a `--server` or `--workspace` that cannot be used:** the bridge still
  starts, and every tool call answers with the reason, which the assistant shows. An exit would
  show in Cowork only as a connector that failed.

The bridge fills the workspace into every call, so no tool asks the assistant for an identifier.
`--server` and `--workspace` in the plugin's `.mcp.json` pin one workspace instead. `--log <file>`
records methods, statuses and reasons, never a token or content, which is the way to see why a
Cowork task cannot reach DevBuddy.

### Codex

Merge `plugins/codex/config.toml` into `~/.codex/config.toml`, and replace **both** example
addresses with the server's. They must match: the helper gives the token only to the server the
checkout is registered to. Put `plugins/codex/AGENTS.md` where Codex will read it.

Codex refuses MCP tool calls in non-interactive mode unless approvals are routed somewhere — `codex
exec --approve-for-me` is the documented way, and an interactive session prompts as usual. Without
it every call comes back `requires approval, but approval policy is never`, which reads like a
server refusal and is not one.

### The administrator's route: stdio

`DevBuddy.McpServer --stdio` still takes a machine token in `DEVBUDDY_TOKEN`, and needs
`DEVBUDDY_ConnectionStrings__DevBuddy`, `DEVBUDDY_Identity__SigningKey` and
`DEVBUDDY_Analysis__RootPath` beside it, because over stdio it is a host over the core and connects
to the database itself. On the devbox it is reached through SSH to a wrapper that runs
`compose run mcp --stdio` inside the stack, with the token kept on the server. Every key in that
wrapper's `authorized_keys` acts as that one token, which is why it is an administrator's route and
not how people install the plugin.

### More than one deployment, or more than one workspace, on one machine

The registry is what separates them: each checkout is registered to one server and one workspace,
wherever it is on the disk, and the credential store holds one token per server and workspace.
A session is one workspace, the one the checkout it was started in is registered to.

Two servers need two `devbuddy` entries in the assistant's configuration, one URL each; the helper
refuses a checkout registered to the other server rather than sending its token there.

`docs/operations/workspace-layout.md` and `templates/devbuddy-root/` describe the arrangement the
stdio route needed, where the environment was the separation and a junction tree was how analysis
found a checkout. Over HTTP analysis reads the working copies on the server, so neither is needed.

### Running either non-interactively

Claude Code needs the tools named before it will call them in print mode:

```bash
claude -p "..." --mcp-config plugins/claude/.mcp.json --allowedTools "mcp__devbuddy__search_knowledge,mcp__devbuddy__create_draft"
```

## After the server is upgraded

Over HTTP there is one `mcp` service, so a session reaches the new server on its next request.
**After `up -d`, check that `mcp` runs the new image**: on the devbox it once stayed on the old
one until `--force-recreate --no-deps mcp`.

Over stdio, **restart every assistant session that was open during the upgrade.** An MCP server
over stdio is a process the session started, and it keeps the version it started with until the
session ends: a local build keeps the old binary loaded, and the devbox's SSH wrapper keeps a
container on the old image. On 2026-09-24 three sessions opened before the devbox moved to
`v1.6.0` were still answering from the old image hours later. On the server,
`tools/release/stale-sessions.sh` lists them and changes nothing. Restart those sessions from the
client; stopping their containers instead cuts an assistant off in the middle of a call.

## Verifying an installation

`devbuddy doctor` in the checkout checks the path end to end. Then ask the assistant for a search.
A working installation answers; a broken one fails in a way that says which part is wrong:

| Symptom | Cause |
|---|---|
| The server fails to connect, and the helper says the folder is not registered | Run `devbuddy register` in the checkout. |
| The helper says the checkout is registered to another server | The URL in the assistant's configuration is not the registered server's. Correct whichever is wrong. |
| The connection fails on the certificate | The machine does not trust the gateway's root CA. For Codex, also `CODEX_CA_CERTIFICATE`. |
| Every call refused with 401 | The token is wrong, expired, revoked, or from before workspace scoping. Mint one and run `devbuddy token set`. |
| Every call refused, saying the credential is not valid in this workspace | The token belongs to a different workspace. Mint one in this workspace. |
| `list_projects` returns nothing | No project has AI access enabled, or the token's owner is not a member of any. |
| A specific project missing | Its owner has not enabled AI access. Working as intended. |
| Analysis reports nothing to analyse | The server's analysis root has no directory named for that project. |
