# G-Able internal npm-default pilot kit

Owner selected npm as default only for this internal pilot on 2026-10-28. Original plugins
under `plugins/` keep their .NET defaults and existing users are not automatically migrated.
Kit preparation is separate from final employee rollout approval.

## Staged files

Owner-private location on the current DevBuddy server:

```text
devbuddy:/home/jm/devbuddy-internal-packages/1.14.0-next.0-99f0c5ea9e92/
```

- `devbuddy-gable-npm-pilot-kit-1.14.0-next.0.tar.gz`: portable setup utility, plugin instruction
  templates, guide and the original tested client tarball.
- `PILOT-KIT-SHA256SUMS`: kit hash; independent of the unchanged client `SHA256SUMS`.
- `kit-result.json`: build inventory.
- `INTERNAL-PILOT-TERMS-DRAFT.md`: accompanying terms; owner accepted content, final checklist pending.

Kit SHA-256: `617b07ca4d6aba7928ee2f32d2657eccd5e7c2060702f3a03852fb9cf4046346`.
Client SHA-256: `99f0c5ea9e923e924c0df27d40583368493f0fbf2533e8af22f26f73f9d1d10f`.
Staging folder/files are owner-only (0700/0600); this is not an employee download service.

## Prepare configurations

Extract the kit on the target machine, verify its checksum against the approved channel,
then from the extracted folder:

```sh
node prepare.mjs --checkout "<existing checkout folder>" --output "<new permanent pilot folder>"
```

Node 24/npm and organization-approved dependency access are required. No .NET/Bun/compiler
is required for preparation. The client installs locally under output/client with dependency
scripts disabled. Absolute Node and installed entry paths are written to the output:

- `claude-plugin/` and `cowork-plugin/`: distinctly named pilot plugin directories.
- `claude-desktop.json`: candidate MCP entry.
- `codex/config.toml`: candidate MCP entry plus unchanged `AGENTS.md`.
- `ACTIVATE.md` and `setup-result.json`: machine-local activation/rollback and preparation metadata.

Keep that output and Node installation at their configured locations. Regenerate if moved.
An existing registration is not required to prepare the kit. Before host activation, register
through the installed client using Plugin access details, or reuse the existing registration.
Instructions/tool descriptions match the source plugins byte-for-byte; the launcher changes.
The default transport is stdio, with explicit checkout directory and DevBuddy home. It invokes
Node `--use-system-ca` directly, avoiding PATH/.cmd/npx-download dependencies at MCP startup.
No CA, credential, registration or active host setting is changed by preparation.

Review and back up the host configuration before selecting the pilot. Use the host's local
plugin installation flow or merge only the generated MCP entry, retaining unrelated settings.
If a host requires a plugin ZIP, package the generated plugin's contents including `.mcp.json`
and `.claude-plugin/` at the expected archive root; do not install the kit's raw templates.
Avoid duplicate DevBuddy integrations in a pilot session. Do not copy an installed output to
another OS/architecture; run preparation there to select its proper native dependencies.

For rollback, restore the prior host launcher/configuration and start a new session. Keep the
shared registry and tokens. The kit does not auto-activate or remove the original integration.

## Verification and remaining acceptance

Windows x64: generated Claude/Cowork/Desktop/Codex profiles each initialize, discover tools
and make a workspace-injected synthetic tool call with empty PATH, an unrelated working
directory, and Thai/spaced install paths. Actual Claude Code CLI health and Codex app-server
discovery pass with isolated app homes and a synthetic-only file-store override; no model turn.
Generated Codex TOML parses. Instruction bytes unchanged. Original plugin guards: 18 passed.

Linux x64: extracted portable kit inventory checksums and all four generated profile synthetic
initialization/list/call cases pass. After the owner started the Mac, exact final kit checksum/
inventory and the same four profile cases pass on Mac ARM64, plus actual Codex app-server
discovery using isolated settings, synthetic file credentials and no model turn. Mac report:
`out/npx-remote-evidence/production-client/macos-internal-default-kit-result.json`.
No claim is made that generated pilot plugins are already installed/activated in the GUI hosts,
that standalone Mac Claude CLI is covered, or that corporate employee network access is verified.

Reports: ignored `out/npx-remote-evidence/production-client/{windows,linux}-internal-default-kit-result.json`.
Maintainer source/test/build: `tools/internal-npm-pilot/{prepare,test,build-kit}.mjs`.

Final kit guide clarifies registration happens after preparation if needed; setup/template/client
bytes are unchanged from the earlier `7aa33085...` kit tested on Windows/Linux.

Support: Teams display name `jannarin meethong` (owner-supplied); pilot approver `jennarin.meethong`.

## Owner-requested BMS Mac installation

On 2026-10-28, owner requested activation for `/Users/jm/Desktop/Works/BMS` in Claude Desktop.
Installed pilot: `/Users/jm/.devbuddy-pilots/bms-1.14.0-next.0`.
Desktop MCP entry `devbuddy` points to that installed client; old `devbuddy-npx-bms-test` removed.
Config backup: `/Users/jm/.devbuddy-pilots/desktop-backup-xxd6rvvm/claude_desktop_config.backup.json`.
The setup did not restart the host. Owner subsequently supplied a fresh Desktop result showing
tool selection/invocation through `devbuddy: list projects`, reporting success and three projects.
Installed BMS Desktop pilot basic invocation is owner-observed accepted; server-audit exact
call count/no-write behavior is not independently reconciled. No broader rollout is implied.

The separately installed user-scope Claude Code plugin `devbuddy@devbuddy-plugin` was observed
enabled and is untouched; it may still be offered in Code/plugin contexts. This is not a claim
of uninstalling or disabling .NET across all hosts/projects. Registry/token remain shared.
Scoped atomic merge utility: `tools/internal-npm-pilot/activate-mac-desktop.py`.
