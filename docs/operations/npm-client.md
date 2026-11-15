# Opt-in npm client (development preview)

## Internal distribution decision — 2026-10-28

Owner selected versioned `.tgz` files for internal G-Able use, with online dependency downloads
through an organisation-approved npm registry/network path. No offline bundle or new private
registry is planned. Tested candidate `1.14.0-next.0` is privately staged on the current
DevBuddy server with checksum and install/update/rollback guide; rollout/version approval and
corporate network verification remain pending. Do not publish to the public repository or npm.

`devbuddy-npx` implements the .NET client's registry, token commands, header helper and stdio
bridge. It is **private and unpublished**. Local Windows x64 checks are recorded in the
[contract checklist](../npx-client-contract.md). Cross-platform production acceptance, real host
acceptance and release prerequisites remain open. Keep using the released .NET client for normal
work until those checks pass. Stage-A evidence concerns the prototype, not this new artifact.

## Build and local evaluation

Requires Node.js 24 and Bun for development. Users of a future packed release need Node, without
Bun, .NET or a compiler. From `src/clients/devbuddy-npx`:

```powershell
bun install --frozen-lockfile --ignore-scripts
bun run typecheck
bun run build
bun test tests
node dist/main.js --help
node tools/client-smoke.mjs
node tools/package-smoke.mjs
```

The smoke tools use isolated synthetic credentials and a temporary loopback HTTP server. On a
sandbox that restricts child-process sockets or rename operations they need normal local execution.
`package-smoke.mjs` downloads pinned runtime dependencies into an isolated cache, with scripts
disabled. It checks the exact tarball inventory and executes the installed entrypoint from a path
containing Thai characters and spaces. This is a local artifact test, not a published-package test.

Use the registration command from Plugin access, replacing `devbuddy` with an absolute call to
`node` and this checkout's `dist/main.js`. The client asks for the token without echoing it; piped
input consumes only its first line. Never put a token in arguments, configuration or shell history.

```text
devbuddy-npx register [path] --server <origin> --workspace <GUID> [--project <GUID>] [--label <name>]
devbuddy-npx list
devbuddy-npx show [path] [--json]
devbuddy-npx update [path] [--server <origin>] [--workspace <GUID>] [--project <GUID>] [--clear-project] [--label <name>]
devbuddy-npx unregister [path]
devbuddy-npx token set --server <origin> --workspace <GUID>
devbuddy-npx token remove --server <origin> --workspace <GUID> [--force]
devbuddy-npx doctor [path]
devbuddy-npx mcp-headers [--dir <path>] [--url <MCP URL>]
devbuddy-npx mcp-bridge [--server <origin> --workspace <GUID>] [--project <GUID>] [--dir <path>] [--log <file>]
```

Paths default to the current directory. Registration discovers `.git` entries without executing
Git; plain document folders are supported too. Home, ancestors of home and drive roots are refused.
Nested registrations use the longest enclosing path. `--clear-project` wins over `--project`;
`--label ""` clears the label. Arguments beginning with `@` are literal paths.

`show --json` returns `path`, `server`, `mcpUrl`, `workspaceId`, `projectId`, `note` without opening
the store or contacting the network. `list` has no `--json` mode. Every command accepts `--help`;
root `--version` identifies the package. Exit codes are 0 success, 1 failure, 2 no registration,
3 helper origin mismatch, 4 unavailable/missing helper or doctor token, and 130 cancellation.

## Shared credentials and rollback

Both clients use `~/.devbuddy/checkouts.json`, schema 1, or `DEVBUDDY_HOME` when configured.
Windows uses Credential Manager; macOS uses the login Keychain. Linux uses the `tokens` directory
with SHA-256 filenames, directory mode 0700 and file mode 0600. Explicit
`DEVBUDDY_CREDENTIAL_STORE=file` selects that store on any OS. Native failures never select it
automatically. Node loads the native module only when credentials are needed.

Unregistering keeps the workspace token. Removal refuses while registrations use it unless
`--force` is given; removal does not revoke it on the server. Use Plugin access to revoke.
Replacement is verified against MCP initialization and `list_projects` before storing it.
An existing rejected token is reported without silently replacing it. Do not run simultaneous
registry writers: neither client shares a locking contract, so concurrent updates may be lost.
JavaScript strings cannot reliably be cleared; explicit write buffers are cleared after use.

To roll back, change the helper or bridge command back to `devbuddy` and start a new host session.
Keep the same user account, `DEVBUDDY_HOME`, server and workspace. No export or token migration is
needed. Uninstalling the npm package does not remove registrations or credentials. The Windows
file-store interchange drill verifies both directions; new native/other-platform rollback drills
remain required before release.

Keychain may prompt after a runtime or client update. Use an interactive desktop session and let
the owner decide access. Locked Keychain, denied interaction or a missing Windows logon session
must be fixed by the user; the client does not unlock stores or change policies.

## Host configuration examples (acceptance pending)

Install the future approved **exact version** in advance and invoke its executable directly.
Do not put `latest` or a download on the host's connection path. Until publication, these examples
can use an unpacked local tarball. Paths and URLs below are placeholders to replace locally.

Claude Code's existing opt-in environment is `DEVBUDDY_CLIENT=devbuddy-npx`. Set it before starting
the host, keep its explicit `--dir` and `--url`, and verify the header-helper timeout from that host.
GUI applications can have a different PATH from a terminal. A Node entrypoint with absolute paths
is the fallback when an npm-generated Windows `.cmd` shim is not executable by the host.

Codex's local MCP configuration can replace its helper while retaining the existing server URL:

```toml
[mcp_servers.devbuddy]
url = "https://devbuddy.example.test/mcp"
http_headers_helper = '"C:\Program Files\nodejs\node.exe" "C:\Tools\DevBuddy\node_modules\devbuddy-plugin-client\dist\main.js" mcp-headers --dir "C:\Work\Customer" --url https://devbuddy.example.test/mcp'
```

Cowork's local opt-in copy of the MCP configuration can use argument arrays:

```json
{
  "mcpServers": {
    "devbuddy": {
      "command": "C:\\Program Files\\nodejs\\node.exe",
      "args": ["C:\\Tools\\DevBuddy\\node_modules\\devbuddy-plugin-client\\dist\\main.js", "mcp-bridge"]
    }
  }
}
```

The bridge chooses explicit server/workspace first, then the current checkout, then distinct
registered workspaces. With several choices it asks the person via `use_workspace`, holds tool
calls until that selection and rejects a change of server/workspace within the session. It injects
the selected workspace into calls and removes that argument from displayed tool schemas. Start a
new task to work with another customer. Its optional log stores timestamp, safe method category
and HTTP status only; tokens, arguments, responses and environment are excluded.

These snippets require actual Claude Code, Codex and Cowork GUI/terminal drills; unit or loopback
transport tests are not evidence that the hosts accepted them. Plugin defaults and web-generated
setup commands remain unchanged.

For occasional use after a release is approved:

```text
npx --yes --ignore-scripts --package=devbuddy-plugin-client@<exact-approved-version> devbuddy-npx doctor
```

## TLS and troubleshooting

Use HTTPS. Plain HTTP is allowed only on loopback. Userinfo URLs and redirects are refused. A
corporate CA must be supplied before starting Node/the GUI host using `NODE_EXTRA_CA_CERTS` with
an absolute PEM path, or the organization's supported Node trust configuration. Do not disable
certificate verification. Token checks time out at 20 seconds; bridge requests at five minutes.
Tool calls are never retried automatically.

For missing registration use `list` and `show --json`; for credentials or connectivity run
`doctor` in the same user session/environment as the host. `show` tolerates an inaccessible store.
A wrong helper origin fails before reading a token and leaves stdout empty. Network and server
payloads are not dumped into errors. Restart the host after changing PATH, CA settings or helper
configuration. Every bridge request rereads the token so replacement/removal is picked up.

Before release, finish the contract checklist, native/SAC production drills, musl tests, real-host
checks, release-artifact SBOM review, package license, publisher ownership, bootstrap and OIDC/provenance setup. This repo
currently provides no license file for the npm artifact; its owner must resolve that release
prerequisite. No publication or default switch has occurred.

Use [the owner preparation template](npm-release-owner-template.md) to record license, controlled
npm name/account and eventual release approval. Unknown values can remain undecided while
development continues. Do not enter credentials in it.

For a local Docker x64 musl drill after building, run `tools/musl-smoke.ps1` from PowerShell.
The driver pins the tested Node Alpine digest, copies only client/package/test-tool files into an
isolated non-root container and retains evidence under `out/musl-smoke-<UUID>`. It never mounts
a Docker socket or a running application's data. This does not test ARM64 or glibc, and the final
release needs its own approved Node security patch and artifact acceptance.

For isolated Unix platform checks, the repository test driver is
`tools/npx-feasibility/run-production-unix.sh <limited-bundle-root>`. Its Windows counterpart is
`run-production-windows.ps1 -TestRoot <limited-bundle-root>`. They use the existing test-only
toolchain, build the real .NET client, and test registry/file-token interchange with the Node CLI.
The Unix runner also exercises a plain document folder. These are test tools, not installed
production commands. Windows PowerShell's native stderr handling is accounted for by checking
exit codes, and any execution-policy override used for the test process changes no persistent
policy or SAC setting.

`node tools/tls-smoke.mjs` requires OpenSSL and creates an ephemeral synthetic certificate. It
tests rejected and accepted CA trust, redirects and dropped connections using only loopback.
`NODE_EXTRA_CA_CERTS` is set only on a test child process; the system CA store is unchanged.

On a Unix test host with Docker, use `sh tools/musl-smoke.sh` after building. This driver selects
the Docker engine's native x64/ARM64 platform and maps the current non-root UID/GID so the
isolated evidence directory remains writable on CI as well. It refuses an image containing a
.NET SDK or C/C++ compiler. Both native architectures have passed this drill; that does not
waive final-artifact security patch, credential-store, host or release acceptance.

## Native artifact and host test tools

`tools/npx-feasibility/dispatch-production-mac.sh <test-root>` runs the production package drill
inside the existing Mac desktop user's GUI session through one temporary LaunchAgent. It never
unlocks Keychain or approves a prompt and removes the job afterward. The Windows counterpart,
`dispatch-production-windows.ps1 -TestRoot <root>`, uses a temporary Limited interactive-user task
and separate read-only SAC snapshots. Both use the source-built .NET client and installed npm
entrypoint, not just the feasibility probe. `tools/native-inventory.mjs <installed-main.js>`
records Node/native hashes without reading a credential.

To close the local x64 policy-read gap, an administrator can run the reviewed dispatcher in an
administrator PowerShell, while the actual native test still runs as the Limited desktop user:

```powershell
& C:\Codes\devbuddy-plugin\tools\npx-feasibility\dispatch-production-windows.ps1 `
  -TestRoot C:\Codes\devbuddy-plugin `
  -NodePath C:\Codes\devbuddy-plugin\out\npx-toolchain\node-v24.22.1-win-x64\node.exe
```

This requires the already built client and isolated Node toolchain at those paths. It does not
change SAC, sign a dependency or change persistent execution policy. The current non-admin
session cannot read active policies; a successful native load alone is not an enforcement pass.
Use `node tools/verify-native-evidence.mjs <evidence-directory>` to independently verify the
reported UTC bracket without rewriting original evidence.

`tools/host-smoke.mjs codex` and `tools/host-smoke.mjs claude` test installed host discovery using
temporary app settings and a synthetic MCP fixture, without a model turn. Supply
`DEVBUDDY_TEST_CODEX_JS` with the installed Codex launcher or `DEVBUDDY_TEST_CLAUDE_EXE` with
the Claude executable. A test wrapper explicitly supplies the synthetic registry; Codex's helper
does not inherit its custom home from the parent process. For a deliberate custom DevBuddy home
in a real Codex setup, validate its effective helper environment or use a local wrapper that
sets that nonsecret path before invoking the installed client. Never embed the token in it.
These connection checks do not replace the pending GUI/Cowork/full-host acceptance drills.

For a desktop host on Node 24 with an organizational CA already trusted by macOS/Windows,
an explicit `node --use-system-ca <client-entry> mcp-bridge ...` lets the child use that
existing OS trust store. A successful Terminal check does not establish that a GUI host
inherits the same CA environment. Alternatively set `NODE_EXTRA_CA_CERTS` to an approved
CA bundle for that host. Keep certificate validation enabled and restart the host after
changing its startup arguments/environment. See
[Node enterprise network configuration](https://nodejs.org/learn/http/enterprise-network-configuration).

On Windows, `node tools/install-lifecycle-smoke.mjs` separately checks an ordinary npm
installation with dependency lifecycle scripts enabled. It limits PATH for its child processes
to Node and System32, asserts compiler/build tools are unavailable, captures the actual Koffi
install hook, and loads the installed native module. It writes an isolated evidence directory
under the client's ignored `out/`; it does not change the system PATH. The dependency may
attempt a source build if its prebuilt native module cannot load, so script-disabled installation
and this lifecycle-enabled drill are separate acceptance checks.

`node tools/runtime-errors-smoke.mjs` exercises actual Node processes against synthetic
loopback HTTP: refusal/rate-limit/server errors, disconnect, unavailable server, preservation
of registry/token after a real 20-second verification timeout, and POSIX SIGINT cancellation.
Set `DEVBUDDY_TEST_LONG_TIMEOUT=1` for the additional actual five-minute bridge timeout;
the drill does not shorten production deadlines or touch real workspace credentials. CI runs
that long case on Linux x64 and the shorter matrix on all configured hosts. Evidence is written
under the client's ignored `out/runtime-errors-*/result.json`.

On Unix, `python3 tools/tty-smoke.py` uses real OS pseudo-terminals and synthetic loopback
HTTP/file credentials to verify hidden token input, Backspace, EOF, Ctrl+C, input length
refusal and terminal restoration. It requires the compiled production client and Node on PATH,
does not read native credentials, and saves metadata-only results under `out/tty-smoke-*`.
