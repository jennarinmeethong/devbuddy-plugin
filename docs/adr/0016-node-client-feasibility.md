# ADR-0016: An opt-in Node client, gated on native credential and SAC feasibility

- Status: **Accepted** 2026-10-24, implementation plan approved by the owner.
- Phase: 14, item 14.21.
- Related: [ADR-0015](0015-devbuddy-client.md).

## Context

The unsigned Windows client cannot reliably serve people with Smart App Control enabled. The
owner approved a TypeScript/Node npm client alongside .NET, preserving OS credential stores and
sharing the existing registry and credentials. Replacing the launcher alone does not establish
SAC compatibility: Node's native modules are binaries too.

## Decision

Implement stage A first under `src/clients/devbuddy-npx`, using a pinned Koffi prebuild and direct
OS calls. On Windows the only libraries are the KnownDLLs `advapi32.dll` and `kernel32.dll`; on
macOS they are fixed absolute Security.framework and CoreFoundation framework paths. No input or
checkout chooses a library, symbol, executable or native module. FFI is confined to reviewed
adapters; the source guard covers product TypeScript, not test orchestration or dependency internals.
This preserves SB-04's ban on repository execution; the existing .NET client already uses direct
native interop. It does not authorise general dynamic loading or process execution.

Use the same generic credential target, persistence and UTF-8 blob on Windows and the same
`devbuddy` Keychain service/account on macOS. No automatic file fallback. Test with random,
synthetic test-only keys, and compare with the actual existing .NET adapters. Native load failure
must remain visible as failure; no fixture substitutes for an OS test.

The eventual package name is proposed as `devbuddy-client`, its command as `devbuddy-npx`, and
its runtime as Node.js 24. Keep the feasibility package private with no plugin-client executable
declaration until the gate passes. A test-only `devbuddy-credential-probe` executable allows local
tarball/npx launcher drills during stage A. Later stages share registry schema 1, add command/bridge parity and opt-in
host configurations, then publish a prerelease under `next` only through the approved release path.
Existing .NET binaries, plugin defaults, web-generated setup commands and server surface remain.

The stage-A gate requires native interoperability and SAC-enforcement drills on Windows x64 and
ARM64, with hashes/signatures recorded. A successful load with SAC off is insufficient. Do not
port the complete CLI before this evidence exists. If the native module is blocked, signing it or
changing credential storage requires another owner decision; neither is an implicit fallback.

## Consequences and current evidence

The Windows x64 prototype and .NET credential exchange pass locally. On 2026-10-25 the owner
confirmed active SAC enforcement through administrator CiTool output after the probes passed
with registry state 1. Koffi's loaded binary remains unsigned; this is a prototype pass on that
machine, not a general signature or reputation guarantee. Stage A completed on 2026-10-25 after
Windows ARM64 native and packed-npx tests passed with independent active-SAC snapshots before
and after the run. The limited-user runner could not read policies, so its original unverified
policy field is preserved and supplemented by separate read-only SSH evidence. No task privilege
elevation or policy changes were needed for the test. On 2026-10-25 the owner requested implementation
of `docs/plan-npx-client.md`. Registry, lazy context, file credentials, CLI, helper and bridge are now
implemented in development with local Windows x64 unit/Node/packed checks and real .NET file-store
interchange. Production bin is `devbuddy-npx`; the probe is packaged separately by test tooling.
[The contract checklist](../npx-client-contract.md) records incomplete acceptance. Real hosts,
other production platforms, native/SAC artifact drills and release gates remain pending; private
packaging and existing plugin defaults remain in place.
Subsequent isolated production file-store tests passed on Linux x64/ARM64 glibc, macOS ARM64
with explicit file opt-in, and Windows ARM64, including the real .NET CLI and installed npm
artifacts. These file-store results do not establish new production Keychain, Credential Manager
or SAC acceptance. See the dated platform section in the contract checklist.
Later on the same day, production installed-artifact native/.NET drills passed on Windows x64,
Windows ARM64 and Mac ARM64. Windows ARM's active-SAC snapshots bracketed the limited-user
production run, verified independently in UTC while preserving an erroneous original collector
boolean. Local Windows x64 still cannot read active policies, so its new artifact SAC gate stays
open. Real-stack npm/bridge tests also pass; full GUI/Cowork and release acceptance remain open.
macOS code is implemented
and its Mac native round trip, packed-npx native tests and cross-client credential exchange now
pass after the owner's interactive Keychain approval. This verifies the tested item/session,
not an absence of future OS prompts after runtime changes or upgrades.
macOS system images without separately readable files are explicitly reported with unavailable
hashes; Node and npm native module hashes remain mandatory. JavaScript strings cannot be reliably zeroed even
though explicit secret buffers are cleared.

See the prototype README for exact commands, versions, native hash, evidence output and outstanding
checks. npm provenance is separate from Windows code signing. No signing certificate, security
policy change, npm publication or automatic migration is authorised by this ADR alone.
