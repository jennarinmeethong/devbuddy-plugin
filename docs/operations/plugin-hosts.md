# Plugin hosts

Three packages, in `plugins/`, over one MCP server. This is what an operator has to know to install
them and, more importantly, what installing them does **not** do.

| | Claude Code | Codex | Cowork |
|---|---|---|---|
| Package | `plugins/claude/` | `plugins/codex/` | `plugins/cowork/` |
| Manifest | `.claude-plugin/plugin.json` | — | `.claude-plugin/plugin.json` (`devbuddy-cowork`) |
| Server configuration | `.mcp.json` | `config.toml` | `.mcp.json`, which starts `devbuddy mcp-bridge` |
| Instructions | `skills/devbuddy/SKILL.md` | `AGENTS.md` | `skills/devbuddy/SKILL.md` |
| Commands | `commands/*.md` | — | — |

The Claude package's commands are `search-knowledge`, `analyze-change`, `draft-record`, `handover`
and, since `v1.13.0`, `review-queue`. Codex and Cowork carry the same guidance in their
instructions, so a person asks in a sentence instead.

Instructions differ between them. Capability does not: all three reach the same MCP server, and a
test asserts that every instruction file describes exactly the tools the server exposes and never
names one it does not. A test derives that list from the catalogue rather than hardening a count, which
is how it caught both packages when semantic search and the embedding sweep took the surface to
twenty.

## What every package tells the assistant

Two pieces of guidance were added after `v1.12.3`, in all three packages, and change nothing on the
server:

- **The review queue (`v1.13.0`, PR #49).** Asked what waits for approval, the assistant lists
  `PendingApproval` records with `list_records` and `view_record_history`, one line each, with the
  link to the record's page. Approving, sending back and publishing stay a person's, on that page.
  The owner decided not to grow the AI surface for this (`info.md`): no `list_work_items` or
  `list_evidence` for AI, and no capture or approval through AI.
- **Search in English as well, answer in the person's language (`v1.14.0`, PR #55, `info.md`
  2026-10-04).** `search_knowledge` matches words, so a question in Thai misses a record written in
  English. The assistant searches with the person's words and again with its English translation of
  them, relies on `search_similar_records` with the question as asked, answers in the language it
  was asked in, says when it translates a record, and never translates a person's words into a
  draft on its own. Nothing stored is translated, because an approval binds the text its approver
  read. `PluginPackageTests` holds every package to it.

The server's half of Thai search is separate: since `v1.14.0` a query holding Thai characters is
matched by substring, so a Thai word inside a phrase written without spaces is found (PR #54).

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
| The URL it checks | `--url`, from the same `DEVBUDDY_URL` as `url` | `--url`, written beside `url` in the file |

Neither relies on `CLAUDE_CODE_MCP_SERVER_URL`. Claude Code redacts the value of any variable it
counts as a credential wherever that value occurs in the URL, and on JMPC one of them held `1`, so
the helper received `https://REDACTED92.REDACTED68…` and refused it as another server (fixed in v1.12.0).

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

`PATH` matters most for Cowork, which starts the bridge as `devbuddy` and finds nothing otherwise.
Claude Code and Codex can name the client another way (`DEVBUDDY_CLIENT`, or the path in
`config.toml`); Cowork cannot. With the folder copied to `~/.devbuddy/bin`, on Windows, once:

```powershell
$bin = Join-Path $HOME '.devbuddy\bin'
$user = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($user -split ';') -notcontains $bin) { [Environment]::SetEnvironmentVariable('Path', "$user;$bin", 'User') }
```

On macOS and Linux, add `export PATH="$HOME/.devbuddy/bin:$PATH"` to the shell's profile. **Then
quit and reopen Claude Desktop**, from the tray or the menu bar and not only its window: an app
keeps the `PATH` it started with, and a Cowork task started before the change still finds nothing.
`devbuddy doctor` in a new terminal shows the client is found.

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

The repository is a marketplace since `v1.12.2` (`.claude-plugin/marketplace.json`), listing the
Claude package alone, so no clone is needed:

```bash
claude plugin marketplace add jennarinmeethong/devbuddy-plugin
claude plugin install devbuddy@devbuddy-plugin
```

`/plugin` inside a session does the same. The marketplace follows `main`, so
`claude plugin marketplace update devbuddy-plugin` brings the package of the latest merge, not only
of a tag. A marketplace made from a local folder, as JMPC's `devbuddy-local` is, still works.

Then set `DEVBUDDY_URL` to the server's address, without `/mcp`, in the environment Claude Code
starts with, for instance `env` in `~/.claude/settings.json`. `DEVBUDDY_CLIENT` names the client if
it is not on `PATH`. Prefer `settings.json` to a shell profile: the Desktop app's Code tab and
background agents do not read `~/.zshrc`.

Claude Code trusts the operating system's certificate store by default (the native installer, or
npm with Node 22.15 or later), so the gateway's root CA installed on the machine is enough. Where
`/mcp` still fails on the certificate, set `NODE_EXTRA_CA_CERTS` to the root CA's file in the same
`env` block; `claude --debug` logs `CA certs: Appended extra certificates from NODE_EXTRA_CA_CERTS`
when it loaded. This was taken from Anthropic's network configuration page on 2026-09-30, not
tried on a machine whose store lacked the CA.

`claude plugin marketplace update devbuddy-plugin` answers `Marketplace 'devbuddy-plugin' not
found` on a machine that never added it; add it first. A marketplace with another name, such as
`devbuddy`, may belong to another repository. The Mac mini had one, from `devbuddy-skill`, with an
unrelated `devbuddy-claude-code` plugin enabled: disable any DevBuddy plugin that does not come
from `@devbuddy-plugin`, so that two sets of tools and instructions do not compete.

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
   that the plugin runs a local process; that process is the bridge. The plugin is
   `devbuddy-cowork`, a name apart from the Claude package's `devbuddy`, because Claude Desktop
   loads a Cowork upload into its Code tab too: under one name, the upload took the Claude
   package's place there. Under two, a Code tab session that has both lists DevBuddy twice. Both
   reach the same workspace for a registered folder, since the Code tab starts the bridge in the
   project folder.
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

**Updating replaces DevBuddy's section of `~/.codex/AGENTS.md`; it never appends again.** Codex has
no plugin to update and no way to include one file in another, and it reads at most 32 KiB of
instructions. The package's file has one top-level heading, `# DevBuddy`, so everything from that
line to the next `# ` heading is DevBuddy's. The handbook's chapter 05 gives the commands for
macOS, Linux and Windows, which keep a `.bak` copy and end by counting that heading, which must be
1. On 2026-09-30 the Mac mini's file held the section twice, from two installs.

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
| The server fails to connect, and the helper says the folder is not registered | Copy the `devbuddy register` command from Plugin access and run it in the checkout, then start a new session. A folder that does not use DevBuddy can be left as it is. |
| The helper says the checkout is registered to another server | The URL in the assistant's configuration is not the registered server's. Correct whichever is wrong. |
| The connection fails on the certificate | The machine does not trust the gateway's root CA. For Codex, also `CODEX_CA_CERTIFICATE`. |
| Every call refused with 401 | The token is wrong, expired, revoked, or from before workspace scoping. Mint one and run `devbuddy token set`. |
| Every call refused, saying the credential is not valid in this workspace | The token belongs to a different workspace. Mint one in this workspace. |
| `list_projects` returns nothing | No project has AI access enabled, or the token's owner is not a member of any. |
| A specific project missing | Its owner has not enabled AI access. Working as intended. |
| Analysis reports nothing to analyse | The server's analysis root has no directory named for that project. |
