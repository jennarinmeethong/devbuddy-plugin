# G-Able internal npm-default pilot kit

This kit prepares a machine-local installation and npm-default plugin configurations.
It is not a public npm release, does not activate plugins automatically, and does not change
the original plugins in this repository. Final rollout approval/readiness checks remain pending.

## Prepare on a pilot machine

Use Node 24 with npm (24.22.1 was used for acceptance). Extract the whole kit, verify its
archive checksum from the approved staging channel, then from the extracted directory run:

```sh
node prepare.mjs --checkout "<absolute checkout folder>" --output "<new permanent pilot folder>"
```

For a custom DevBuddy registry, explicitly add `--home "<existing client-home folder>"`.
The default registry is the operating-system user's `.devbuddy`. No token is read, copied or
changed during preparation. The checkout must exist, but need not already be registered.
After preparation, use the installed entry recorded in setup-result.json to run registration
with details from Plugin access before activating the host. Existing registrations can be
reused. No .NET client is required to create a new registration.

The utility verifies the exact tested `.tgz` hash, installs it into the new output's `client/`
with dependency scripts disabled, and creates these configuration candidates:

- `claude-plugin/`: local Claude Code plugin, npm stdio client as its default.
- `cowork-plugin/`: local Cowork plugin, npm stdio client as its default.
- `claude-desktop.json`: entry to merge into Claude Desktop settings after backup.
- `codex/config.toml`: entry to merge into Codex settings after backup.
- `ACTIVATE.md` and `setup-result.json`: local activation/rollback notes and preparation metadata.

Instructions/tool descriptions are copied unchanged from the original plugins. Plugin names
are distinct so the pilot is identifiable. Their MCP launcher uses the current Node executable
and installed JS entry through absolute paths and argument arrays. No `devbuddy` .NET launcher,
Windows `.cmd` shim, PATH lookup or npx download is needed on the MCP connection path.

## Activate only in the approved pilot scope

Review the generated files and back up the current host settings. Install only the generated
plugin for the appropriate host, or merge its MCP entry; preserve unrelated host settings.
The generated `devbuddy` MCP entry intentionally represents the pilot's primary DevBuddy client.
Avoid enabling both original and pilot DevBuddy integrations in the same pilot session.
Nothing here installs a host app, logs into an account, overrides permissions or changes
an existing employee configuration. Host-specific installation/approval is a separate step.

Keep the installed output folder and Node at the same paths. Regenerate configurations if
either location changes. Node uses the existing system CA store with `--use-system-ca`;
an organizational CA still needs to be approved/trusted on that machine. Keep TLS verification
enabled. GUI Keychain prompts must be reviewed by the user normally.

First installation needs approved npm registry access, including any required proxy/CA setup.
This kit includes the client tarball but not a complete offline dependency cache. It does not
set a global npm prefix or install/update .NET, Bun or a compiler.

## Verify and roll back

After starting a new host session, use the pilot integration for one read-only `list_projects`
call and report only success/count. Do not submit tokens or project payloads in issue reports.
For rollback disable the pilot plugin or restore the previous host MCP configuration, then
start a new session. Keep the shared registry and tokens; do not unregister or remove them.

Internal pilot support: Microsoft Teams display name `jannarin meethong` (owner-supplied).
Audience: G-Able personnel as specified by the owner. Pilot approver: `jennarin.meethong`.
Internal terms and readiness checklist remain separate accompanying review documents.

## Maintainer build/test

From this repository, use the approved tarball path explicitly:

```sh
node tools/internal-npm-pilot/test.mjs <tested-client.tgz>
node tools/internal-npm-pilot/build-kit.mjs <tested-client.tgz> <new-output-folder>
```

The build output contains a portable kit plus its `.tar.gz`/checksum. The installed `client/`
folder generated on one machine must not be copied to another OS/architecture; run preparation
on each target machine so npm selects the appropriate native dependency.
