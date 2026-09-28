# ADR-0015: A `devbuddy` client holds the plugins' credential, per checkout, outside every repository

- Status: **Accepted** 2026-09-27 (`info.md`). The owner took each decision below in conversation;
  this records the route.
- Date: 2026-09-27
- Phase: 14 (A4)
- Related: [ADR-0006](0006-mcp-dual-transport.md), whose amendment of the same day makes the HTTP
  transport take a machine token.

## Context

The plugins reached the devbox over SSH to a stdio wrapper that read one token from the server.
Every key in `authorized_keys` ran as that token, so a second person acted as the first, and an
administrator had to place each person's token for them. The owner chose HTTPS through the gateway
instead, with each person's own machine token (`info.md`, 2026-09-27).

That moves the token onto the person's machine, and raises four questions this ADR answers: where
the token is kept, how an assistant finds the right one for the checkout it was started in, how
Claude Code and Codex share it, and what stops it going to the wrong server.

What the two assistants offer decides the shape. Both accept a *header helper*: a command whose
standard output is a JSON object of headers.

- **Claude Code** (`headersHelper`) runs it in a shell with a 10-second timeout, at connection and
  again after a 401 or 403. For a plugin's server it runs in the **plugin's** folder, not the
  project's, and sets `CLAUDE_CODE_MCP_SERVER_URL`. `${CLAUDE_PROJECT_DIR}` is expanded in the
  field. For servers from a plugin or a project file it removes variables whose names contain
  `TOKEN`, `SECRET`, `KEY` and similar.
- **Codex** (`http_headers_helper`) runs it and caches the headers for the connection, refreshing
  once after a 401 or 403.

## Decision

**A .NET console, `devbuddy`, in `src/clients/DevBuddy.Client`.** It references no project in this
repository: it holds no database connection and no signing key, and talks to the server only over
HTTPS.

**A per-user registry, `~/.devbuddy/checkouts.json`**, maps a checkout's folder to a server origin,
a workspace and, optionally, a default project. It is outside every repository, so no pull request
can change which server a token is sent to. A checkout may be anywhere. On Unix the file is mode 600
in a mode-700 folder. A checkout is the git repository holding the folder, or, outside one, the
folder itself (`info.md`, 2026-09-28), so somebody who works in documents rather than code can
register too. The root of a drive, the home folder and a folder above it are refused, as a
repository root or a plain folder, because every session under them would carry the token.

**The token is kept in the operating system's credential store**, keyed by server origin and
workspace: Credential Manager on Windows through advapi32, and the login keychain on macOS through
Security.framework, both called directly. **On Linux it is a mode-600 file** in a mode-700 folder.
The client starts no process, because product code may not (SB-04, `NoExecutionTests`): that rules
out `security` and `secret-tool`, and libsecret's own calls take variable arguments, which interop
does not marshal portably. `info.md` named Secret Service or a file; the file is what Linux gets
until Secret Service can be reached another way. It is never an argument: it is typed at a hidden prompt, or read from standard
input when that is not a terminal. It must be 43 base64url characters, the shape the server mints.

**Commands:** `register`, `list`, `show`, `update`, `unregister`, `token set`, `token remove`,
`doctor`, and `mcp-headers`, the helper. `register` and `token set` check the token against the
server before storing it: it must be accepted, and `list_projects` in the workspace must not be
refused.

**The helper releases a token only to the server the checkout is registered to.** `mcp-headers`
takes the folder (`--project`, else the working directory) and the MCP URL (`--url`, else
`CLAUDE_CODE_MCP_SERVER_URL`). It finds the registered checkout that contains the folder, the
longest match winning, and prints the `Authorization` header only if the URL's origin is that
checkout's server. Otherwise it prints nothing to standard output, says why on standard error, and
exits non-zero: an unregistered folder, a missing token, or a URL that is not the registered server.
The last one is what keeps a mistaken or altered configuration from sending the token anywhere else.

**`mcp-bridge` is for an assistant that runs no header helper** (2026-09-28, on trial). Cowork reads
only `url`, `headers` and `oauth` from a plugin's remote entry, but starts a plugin's local stdio
server on the host. The bridge is that server: each JSON-RPC line goes to `/mcp` with the token
stored for that server and workspace, read from the store per request. Cowork starts it in the
system folder and tells it no folder, so it takes, in order, a `--server` and `--workspace` it was
given, the registered checkout holding the folder, or the one server and workspace every
registration on the machine shares, and refuses when there are two. The token is keyed by server,
so a configuration naming another server finds none to send. It supplies the workspace itself: it takes
`workspaceId` out of the tool schemas and puts the checkout's into every call, replacing whatever
the assistant wrote, which grants nothing because the token is refused in any other workspace. In
Cowork an assistant never saw the server's `instructions` and guessed one. The default project is
still named in those instructions.
`--log` notes methods, statuses and reasons, never a token or content.

**One session is one workspace.** The helper resolves the token when the assistant connects, from
the folder it was started in.

## Consequences

- Claude Code and Codex read the same registry and the same token.
- A person registers each checkout once, and pastes a token once per workspace.
- The token is on the person's machine. The credential store protects it at rest; a process running
  as that person can still ask the store for it, as it could for any of their credentials.
- The client is unsigned (B2). On Windows with Smart App Control on it cannot run, and the release
  notes say so.
- The plugin packages move to HTTP. SSH to the stdio wrapper stays documented as the administrator's
  path and the fallback.

## Alternatives considered

- **Settings committed in the repository.** Refused by the owner: a file in a repository is edited
  by pull requests, and one that chose the server would let a pull request redirect the token.
- **A token per project.** Refused by the owner: the workspace is the unit a token is scoped to.
- **Binding a token to a MAC address.** The server never sees the client's MAC through the gateway
  or the VPN, and a reported one is a claim anybody holding the token can copy.
- **An environment variable, `bearer_token_env_var`.** Works from a terminal, but GUI launches do not
  inherit a shell's environment, and every command the assistant runs would inherit the token.
- **TypeScript on Bun, to avoid signing.** Considered because Smart App Control cannot be excepted
  per program. The owner chose .NET and keeps Smart App Control off on their own machine.
- **OAuth for MCP.** Both assistants support it, and it would remove pasting a token. Named as a
  later step: it makes DevBuddy an authorization server, which is a larger surface to own.
