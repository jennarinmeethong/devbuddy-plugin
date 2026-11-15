# npm client contract and acceptance checklist

Baseline: `src/clients/DevBuddy.Client/{Commands,Registry,CredentialStores,ServerOrigin,ServerCheck,McpBridge,ClientContext}.cs`.
Approved scope: [plan-npx-client.md](plan-npx-client.md). The table assigns an acceptance owner to
every feature family; a pending test is not a completed feature claim.

**Latest artifact after the prompt-order correction:**
`99f0c5ea9e923e924c0df27d40583368493f0fbf2533e8af22f26f73f9d1d10f`.
Installed native/.NET checks pass Windows x64/ARM64 and Mac ARM64; package/npx checks
also pass Linux x64/ARM64 and native musl x64/ARM64. Fresh ARM64 SAC evidence passes for
this artifact; fresh x64 SAC also passes the owner-run administrator collector and independent
verification. Earlier full-host
acceptance binds to the prior `62d1...` artifact and remains historical. Read-only archive
comparison confirms only `dist/token-input.js` differs; all 13 other packaged files match.

| Contract / work package | Test owner and current evidence | Remaining acceptance |
|---|---|---|
| B0 CLI command/options/output baseline | `tests/client.test.ts`: per-command help, invalid args, show JSON field fixture, lifecycle/exit checks; `tools/client-smoke.mjs`: Node CLI vs .NET | Complete parser/output fixtures for every error/options combination |
| B1 lazy context, registry schema 1 | Unit readonly/corrupt/future/longest-match/atomic-failure checks; actual Unix 0700/0600 verified on Linux/macOS production drills | Cross-client concurrent writes intentionally unsupported |
| B1 folder discovery | Git/worktree/missing/broad-root tests; actual Unix non-Git-folder drills; platform case/separator fixtures pass Windows/Linux/macOS | Final artifact regression |
| B1 .NET registry interchange | Actual production .NET/Node interchange passes Windows x64/ARM64, Mac ARM64 and Linux x64/ARM64 | Final published artifact must repeat rollback |
| B2 native credentials | Installed production native/.NET exchange passes Windows x64/ARM64 and Mac ARM64; both Windows SAC brackets verified | Fresh macOS download/Gatekeeper path remains unverified; full desktop prompt/cancel matrix |
| B2 file credentials | SHA-256 read/replace/delete and .NET interchange; Unix modes verified on glibc x64/ARM64 and native musl x64/ARM64 | Final release artifact regression |
| B2 input and token lifecycle | Unit checks plus actual Unix PTY hidden enter/Backspace/empty and typed EOF/Ctrl+C/oversized input and full terminal restoration on Linux x64/ARM64 and Mac ARM64 | Windows console and native-store prompt/cancel matrix remain |
| B3 register/list/show/update/unregister/token/doctor/help/version | Unit command tests plus real Node loopback HTTP/stdio smoke, including real .NET rollback | Expanded CLI baseline fixtures; all platforms |
| C1 origin/TLS/helper | Unit origin/refusal/output checks; actual CA/redirect/interruption drills Windows/Linux/Mac; Mac real-host system-CA correction tested | Complete host-specific helper timeout/PATH matrix |
| C1 token verification | Real throwaway DevBuddy rotation/revocation/wrong-workspace tests pass; malformed envelope/results refused in unit tests | Final artifact regression |
| C2 bridge routing/choice | explicit target validation, hold calls until choice, reject session workspace/origin switch, readonly unresolved values | Missing/ambiguous choices and simultaneous choices across full platform matrix |
| C2 injection/schema | Unit and real throwaway stack nested override tests pass; actual catalog matches server allow-list and remains unchanged | Final artifact regression |
| C2 protocol | Unit SSE/concurrency/EOF checks; real Node disconnect and Unix SIGINT pass; actual five-minute bridge deadline passes on Linux x64 | GUI/terminal keyboard Ctrl+C matrix remains distinct from POSIX signal delivery |
| C2 diagnostics/logs | Safe log/unit checks; actual Node HTTP error/disconnect/unavailable/20-second verification and five-minute bridge deadlines preserve token/registry with no retries | Full host-level error/lifecycle matrix remains |
| Security SB-04 | recursive product TS/JS guard and fixed native-library allow-list; actual child_process mutation in product `.js` failed guard and revert passed (2026-10-25) | Broader aliases/dynamic-loading guards and required CI evidence; dependency internals are outside guard |
| D real hosts | Mac BMS owner-observed Claude Desktop/Cowork/Codex/Claude Code Desktop basic calls pass; Windows isolated helper/discovery passes; [guide](operations/npm-client.md) | Broader host lifecycle/error/rollback and full PATH/.cmd matrix; standalone Mac Claude CLI not covered |
| E1 production package | Clean tarball/script-disabled local/global/cold-warm npx/SBOM passes all six tested platforms; Windows x64 lifecycle-enabled install passes without compiler | License/dependency obligations; final security-patched release artifact; lifecycle-enabled installs on remaining platforms |
| E1 prototype regression | separate private generated feasibility package; existing cold/warm npx launcher and negative cases pass | Native regression on each target after production changes |
| E2 CI/release | Workflow includes Windows/Linux ARM64, native musl x64/ARM64, .NET interchange/TLS; local actionlint passes | GitHub CI not yet run; release checks, owner-configured trusted publisher and OIDC workflow |
| F pilot | Private, unpublished; .NET defaults preserved | Owner-approved release/version, name ownership, provenance/hash/tag checks, published-artifact tests and rollback |

Intentional differences: npm executable `devbuddy-npx`; dynamic-loading scope is the approved FFI
adapters; safer errors do not reflect arbitrary arguments or remote payloads; corrupt/unsupported
registry versions are rejected instead of .NET's permissive version reading; unique temporary
filenames avoid collisions without promising cross-client concurrent updates. `list --json` is
unsupported, matching .NET. `show --json` retains exactly the .NET fields and no native dependency.

## 2026-10-28 scoped internal npm-default kit

Owner authorised npm default only for the G-Able pilot kit. Added local install/configuration
utility and portable kit builder under `tools/internal-npm-pilot/`, with a fixed tested tarball
hash, existing-checkout/new-output checks and no active host/credential/registration writes.
Generated plugin instructions are copied unchanged; launchers use absolute Node and installed
entry paths over stdio. No npx fetch/PATH/.cmd execution is used on the connection path.

Windows and extracted Linux kit profile integration passes all four generated profiles,
including empty PATH/wrong cwd/Thai-space paths/workspace injection. Windows actual Claude
Code CLI health and Codex app-server discovery pass in isolated synthetic settings, no model
turn. Original PluginPackageTests: 18 pass. Generated TOML syntax verified. Mac connection
timed out; kit-specific Mac acceptance remains pending. No active employee host migration.
See [kit guide](operations/npm-internal-pilot-kit.md) for hashes, staging and limits.

After owner brought Mac online, final kit `617b07ca...` checksum/inventory passed there.
Installed client/profile checks pass on Mac ARM64 for Claude/Cowork/Desktop/Codex with empty
PATH, wrong cwd, Thai/spaced paths and synthetic workspace injection. Actual standalone
Codex app-server discovery passes in an isolated app home, no model turn. Report retained in
ignored `out/npx-remote-evidence/production-client/macos-internal-default-kit-result.json`.
This closes the kit-specific Mac setup/discovery gap; it does not activate live GUI profiles,
retest every real-host lifecycle or verify corporate employee registry/network access.

Subsequent owner-requested BMS activation installed the approved `99f0...` client under
`/Users/jm/.devbuddy-pilots/bms-1.14.0-next.0` and changed only the Desktop MCP entries,
with private backup. Owner screenshot then visibly selects/invokes `devbuddy: list projects`
and reports success/three projects. This is fresh installed-pilot Desktop basic invocation,
owner-observed, excluding prior plugin/test names in the prompt. No project payload is copied
here and no independent server audit count/no-write guarantee is claimed. User-scope .NET
Code plugin remains installed/enabled; no uninstall or other-host activation occurred.

## 2026-10-25 local verification

### Prompt-fixed artifact Windows x64 SAC verification

Owner-run administrator collector completed for the same **99f0c5ea...** artifact. Actual
native/.NET/package test used Limited task level and exited zero. Independent verifier passes
active SAC snapshots around the run: before 16:26:09.5361480Z, start 16:26:09.6282431Z,
finish 16:26:20.0908823Z, after 16:26:20.1074012Z. Both Windows architectures now have
fresh SAC evidence for this exact artifact; no general unsigned-module guarantee is implied.
Original evidence preserved at repository-root `native-evidence-65c94857900b43a1bbd32165844f2506`;
unchanged copy plus separate verification retained in ignored
`out/npx-remote-evidence/production-client/native-evidence-65c94857900b43a1bbd32165844f2506/`.

### Prompt-fixed artifact Windows ARM64 SAC verification

The initial script-policy refusal was the Windows client default: every policy scope was
Undefined (effective Restricted), with no configured MachinePolicy/UserPolicy. Dispatcher
and Limited-user task now use `RemoteSigned` for their own PowerShell processes, instead
of the prior child `Bypass`; no user/machine registry policy or SAC change was made.
Native/.NET/installed package checks pass for artifact **99f0c5ea...**. Independent verifier
confirms active SAC snapshots bracket the run: before 16:22:23.6035379Z, start
16:22:24.3917268Z, finish 16:22:47.7526867Z, after 16:22:47.7973736Z. New standalone
policy inspection shows all scopes Undefined again and zero temporary DevBuddyNpxProduction
tasks. Original evidence plus independent report retained locally in ignored
`out/npx-remote-evidence/production-client/native-evidence-1e730cfe77444f43892326287469e465/`.
Owner will run the x64 administrator collector; not counted complete until its files verify.

`tools/artifact-diff.mjs` reads gzip/tar without extracting or executing archive content.
Comparison of tested **62d1...** and **99f0...** artifacts finds no added/removed files,
only `package/dist/token-input.js` changed, 13 files unchanged (including package metadata,
bridge, HTTP and native adapters). Report retained at
`out/npx-remote-evidence/production-client/prompt-artifact-diff.json`.

### Actual PTY input and prompt-order correction

The first real PTY observer found the visible prompt could precede disabling terminal echo.
Waiting for raw-mode activation passed all six cases, identifying a timing gap rather than
persistent echo. Production `readToken` now enables raw mode before writing its prompt, and
puts prompt writing inside the restoration try/finally. Regression assertion fails when the
old ordering is temporarily restored; corrected code passes. Full local suite **43 tests,
455 assertions**, strict production/test checks pass. New tarball hash is **99f0c5ea...** above.

Without any readiness delay, actual PTY tests now pass six cases on Linux x64/ARM64 and
Mac ARM64: enter, Backspace, empty EOF, typed EOF, Ctrl+C, oversized input. Echo is disabled
when the prompt is observed; all terminal attributes restored; refused/cancelled input does
not contact the synthetic server or persist token/registry. Local retained reports:
`out/npx-remote-evidence/production-client/{linux-x64,linux-arm64,macos-arm64}-pty-result.json`.
CI now includes this Unix PTY drill (workflow YAML syntax checked; GitHub execution pending).

New artifact installed tests pass Windows x64 native/.NET (`out/package-smoke-WSk8pm`),
Linux x64 (`package-smoke-roszR9`), Linux ARM64 (`package-smoke-S7tQSt`), Mac ARM64 file/native
(`package-smoke-g6UZzH`, `package-smoke-qbheff`), Windows ARM64 file (`package-smoke-FSYZJ6`),
and patched musl x64/ARM64 (`musl-native-iVM97j`, `musl-native-cOkXLC`). All hash values match.
After owner approved Keychain, Mac native desktop rerun exited zero; its temporary LaunchAgent
was removed. Retained evidence: `out/npx-remote-evidence/production-client/native-mac-SLdaZx/`.
Windows ARM64 script launch was refused by PowerShell policy; direct Node package drill first
failed because npx could not find Node on PATH, then passed with a child-shell-only PATH fix.
No script/security policy was changed; native/SAC final-artifact reruns remain pending.

### Patched musl image, advisory audit and argument/path regressions

- Production installed dependency `npm audit --omit=dev --json` reports zero known advisories
  at this check. Retained result:
  `out/npx-remote-evidence/production-client/npm-production-audit-20261025.json`. This is
  advisory evidence for the installed lock graph, not a guarantee of absence of vulnerabilities
  or completion of license review.
- Replaced the old musl drill image with official multiarch index
  `node@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53`, observed
  Node **24.22.1**. Actual native x64 and ARM64 compiler-free/non-root packed install,
  cold/warm-offline npx, SBOM, file permissions and HTTP/stdio drills pass on the respective
  approved hosts. Same client artifact SHA-256 `62d1ebcab798e227e0c514f88bc28a38df30013e84b9d883209fccd8977fcfda`.
  Local evidence `out/npx-remote-evidence/production-client/linux-{x64,arm64}-musl-node24221-result.json`.
  Original remote drill directories: `musl-native-ZUnEkd` (x64), `musl-native-ehwCft` (ARM64).
  Both musl launcher defaults now pin this tested index. Earlier 24.21.0 evidence remains historical.
- Added 28 invalid-command cases proving no prompt/store/network activity, unchanged registry,
  and safe errors, plus actual platform case/separator matching fixtures. Windows suite:
  **43 passed, 453 assertions**; strict test typecheck passed. Linux client suite:
  **33 passed, 384 assertions**, including the case-sensitive path branch; native Mac ARM64
  also passed **33/384** with its .NET-compatible case rule. Production JS
  unchanged. Other manual input/host matrices remain explicitly pending.

### Continued runtime errors and rollback verification

`tools/runtime-errors-smoke.mjs` passed on Windows x64 Node 24.22.1 with synthetic-only
loopback traffic: actual HTTP 401/403/404/405/429/500/503, socket disconnect, unavailable
endpoint and the actual 20-second verification timeout (20,074 ms). Each refusal issued one
request; no retries, synthetic secret/upstream body in output, registry changes or loss of the
old token on failed replacement. Evidence: npm-project `out/runtime-errors-L3bPUS/result.json`.

Linux x64 Node 24.22.1 passed the same cases plus actual SIGINT during an in-flight bridge
call (exit 130) and the production five-minute bridge deadline (300,110 ms, one initialize
plus one tool request, timeout response and no retry). Verification deadline was 20,064 ms.
Evidence retained locally in ignored
`out/npx-remote-evidence/production-client/linux-x64-runtime-errors-result.json`; original
remote npm-project `out/runtime-errors-g6rXyl/result.json`. Production deadlines were not
shortened. This does not claim a physical keyboard/GUI Ctrl+C test or real-host error matrix.

Strengthened `tools/client-smoke.mjs` to return to the actual .NET MCP bridge, receive its
SSE tool response, then replace a token through npm and verify it through .NET doctor.
Windows x64 native Credential Manager and Linux x64 file-store/plain-folder variants both
passed with 24 synthetic HTTP requests and cleanup. Evidence: npm-project
`out/client-smoke-RRWAxe/result.json` (Windows) and Linux
`out/client-smoke-ArLt7X/result.json`. This is executable transport rollback evidence,
separate from a published npm artifact rollback or a real host session switching clients.

CI now runs the runtime error/deadline drill on its configured OS matrix; only Linux x64
enables the additional real five-minute case. Changed YAML parses successfully with Bun;
the modified workflow has not run on GitHub. Production JS and package artifact are unchanged.

- TypeScript strict production and test checks pass; 33 Bun tests pass (172 assertions).
- Existing .NET client regression suite: 90 passed, none skipped. Sandbox testhost socket refusal
  required rerunning the same suite outside the sandbox.
- Real Node 24.21.0 Windows x64 smoke with .NET: 14 HTTP requests; shared schema/file credentials
  read and replaced both ways, helper boundary, bridge SSE/EOF and rollback pass. Evidence:
  ignored `src/clients/devbuddy-npx/out/client-smoke-TnbcJ9/result.json`.
- Production tarball installed with scripts disabled and evaluated on isolated Node 24.22.1
  Windows x64. Exact inventory and installed CLI/HTTP smoke passed; dependency entrypoint removal
  proved help/list do not load Koffi. Evidence: ignored `out/package-smoke-aJPBB8/result.json`
  inside the npm project. This preliminary artifact predates subsequent diagnostics/docs changes;
  each release must test its own final tarball.
- Separated feasibility cold/warm npx help and negative cases pass on Node 24.22.1; evidence:
  ignored npm-project `out/npx-portable-0LWU37/result.json`. No new SAC claim from this run.
- Updated production artifact: local and isolated-global installs, cold/warm-offline production
  npx, CycloneDX dependency SBOM and installed CLI/HTTP smoke pass on Node 24.22.1 Windows x64.
  Evidence: npm-project `out/package-smoke-u0KeKw/result.json` and `sbom.cdx.json`; tarball
  SHA-256 `7065e7d3729336c885ca10e1a131ec1e5780336bb12d59a294a88dc429a42ad0`.
- Production CLI also passes native Windows Credential Manager integration on Node 24.22.1,
  with a random synthetic workspace: register, helper, bridge, replacement, doctor and .NET/Node
  native-token interchange both directions, then unregister/remove and final cleanup. Evidence:
  npm-project `out/client-smoke-LUVm4w/result.json`. This is a native functional test, without a
  new SAC-policy snapshot; it does not complete artifact/SAC acceptance on other machines.

These are local development results. B–E are in progress, F is pending, and no production
cross-platform/real-host acceptance is inferred from fake transports or stage-A evidence.

## 2026-10-25 follow-up while owner prepares publication details

This dated evidence supersedes the corresponding earlier pending cases in the table above.

- Added [the owner preparation template](operations/npm-release-owner-template.md), with no request
  for passwords, OTPs or tokens. Unknown license/account details do not block development tests.
- Fixed an open SSE connection after verification returned its matching result. A regression
  proves both initialize/tool response bodies are cancelled. Malformed JSON-RPC envelopes and
  malformed tool results are refused even with HTTP 200.
- Strict TS checks and **37 Bun tests / 240 assertions** pass. HTTP 3xx/401/403/404/405/429/5xx,
  TLS/network error mapping and cancellation now have safe-error tests; actual TLS/CA and
  connection-interruption drills remain pending.
- Expanded source guards cover computed/aliased eval, Function, require/createRequire,
  getBuiltinModule, FFI imports/load aliases, unreviewed modules and Bun/Deno execution references.
  Actual product JS `globalThis["eval"]` mutation failed; removing it restored the pass. This
  remains a structural source check, not a sandbox for dependencies or arbitrary metaprogramming.
- Updated Windows x64 Node 24.22.1 artifact passes local/isolated-global installs, cold/warm-offline
  npx, SBOM and installed HTTP/stdio smoke: `out/package-smoke-6UPTNk/result.json`, SHA-256
  `5fe5bed15f2c6edc0190ca64eb0657e21c65d9f7519637f8005501479905bf8d`.
  .NET file-token/registry interchange also passes again: `out/client-smoke-FSokTq/result.json`.
- **Linux x64 musl package drill passes** in local Node Alpine, as non-root with dropped
  capabilities, read-only input and isolated temporary storage. Image pinned to
  `node@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1`;
  Node 24.21.0, musl 1.2.6. Local/isolated-global installation, cold/warm-offline npx, SBOM,
  installed CLI/HTTP/stdio and actual Unix 0700/0600 checks pass. Evidence under npm project:
  `out/musl-smoke-245acb57-9439-4947-a60d-367a8be42fee/evidence/package-smoke-meKceo/result.json`.
  This is x64 musl development evidence only, without .NET interchange or native ARM64 acceptance.
  The image's Node patch is not claimed to pass future release security checks.

## 2026-10-25 production platform continuation

The owner requested continued implementation. Limited client source/config/test bundles were
transferred to the existing test hosts, into a new directory under `.devbuddy-npx-test`.
Prior prototype evidence and running deployments were preserved. Test runners are checked in as
`tools/npx-feasibility/run-production-unix.sh` and `run-production-windows.ps1`.

| Host | Production file-store checks | Native-store acceptance |
|---|---|---|
| `devrelease`, Linux x64 glibc | Node 24.22.1; 39 tests, 271 assertions; .NET registry/token interchange both directions; ordinary and non-Git folder; actual 0700/0600 modes; installed package/npx/SBOM pass | Uses the intended Linux file store |
| `ubuntu`, Linux ARM64 glibc | Same checks pass natively on ARM64; 39 tests, 271 assertions | Uses the intended Linux file store |
| `macmini`, macOS ARM64 | Same checks pass with explicit file-store opt-in; 39 tests, 271 assertions | Production Keychain desktop/packed/interchange drill still pending; file-store result is not Keychain evidence |
| `winarm`, Windows ARM64 | Node 24.22.1; 39 tests, 269 assertions; .NET registry/file-token interchange and installed package/npx/SBOM pass | Production Credential Manager desktop/packed/SAC drill still pending; prototype evidence is not substituted |

Each package drill performs local and isolated-global installation, cold/warm-offline npx,
exact inventory checks, SBOM generation and installed HTTP/stdio integration. The installed
drills also use the real source-built .NET CLI for file-token/registry compatibility.
Local summaries are retained under ignored `out/npx-remote-evidence/production-client/`, named
`<host>-client-result.json`, `<host>-package-result.json`, `<host>-unit-tests.txt` and, on Unix,
`<host>-plain-folder-result.json` and `<host>-tls-result.json`.

Comparing to the actual .NET CLI exposed missing parser behavior. Added token-group help,
`-?`, boolean values such as `--json=false` and `--json false`, the `--` terminator for literal
paths, and GUID normalization for D/N/B/P forms. The updated suite passed on all four remote
hosts. Unknown options remain safely refused; npm error text does not echo arbitrary input.

Actual HTTPS tests now pass on local Windows x64, Linux x64, Linux ARM64 and macOS ARM64:
untrusted synthetic CA refuses before retention, per-process `NODE_EXTRA_CA_CERTS` accepts,
307 redirects are not followed, and a dropped connection is not retried. No system trust store
was changed; generated synthetic private keys are removed after the drill. The local Windows
report is npm-project `out/tls-smoke-9RZH7P/result.json`.

CI now declares Windows x64/ARM64, Linux x64/ARM64, macOS ARM64 and native x64/ARM64 musl jobs,
with source-built .NET interchange and Unix TLS drills. The pinned Alpine manifest was checked
to contain amd64 and arm64 variants. The workflow passed local actionlint validation; it has
**not run on GitHub**. Native ARM64 musl, real DevBuddy stack/host acceptance, native-store
desktop/artifact/SAC checks and release prerequisites remain open.

The subsequent **native x64 and ARM64 musl runs now both pass**, using the finalized
`tools/musl-smoke.sh` on `devrelease` and `ubuntu`, respectively. The driver checks the Docker
engine architecture, selects that same platform explicitly and uses the invoking user's non-root
UID/GID for writable evidence mounts (including CI runners whose UID differs from 1000).
The pinned Alpine runtime has Node 24.21.0 and no .NET SDK or cc/gcc/g++/clang. Script-disabled
local/global installation, cold/warm-offline npx, SBOM, installed CLI/bridge and Unix modes pass.
Both musl tarball hashes match the four glibc/macOS/Windows platform artifacts:
`62d1ebcab798e227e0c514f88bc28a38df30013e84b9d883209fccd8977fcfda`.
Retained results: `out/npx-remote-evidence/production-client/linux-x64-musl-result.json` and
`linux-arm64-musl-result.json`. These supersede the pending ARM64 musl entry above, without
claiming a release security-patch approval or completing native credential/real-host acceptance.

## 2026-10-25 real stack, native artifacts and host connections

**Real-stack acceptance:** `tests/e2e/specs/npm-client.spec.ts` now runs the built production CLI
and bridge against fresh API/MCP/PostgreSQL/SeaweedFS containers. Both tests and their setup pass
(three Playwright results), followed by all eight existing stdio checks. The npm tests prove real
token registration, wrong-workspace refusal, the unchanged AI catalog, top-level/nested workspace
injection, replacement within a live bridge, revocation and multi-workspace selection/locking.
The test-only loopback gateway relays actual MCP responses; it does not manufacture them. Results:
ignored `out/npx-real-stack-20261025/{junit.xml,stdio.log,stack.log,report/index.html}`. The stack's
containers and volumes were removed after completion. GUI host acceptance is separate.

The initial real-stack build was refused by NuGet audit for transitive
`System.Security.Cryptography.Pkcs` 10.0.0. Central transitive pinning now selects **10.0.13**,
the patched version for [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)
and [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx). Audit remains enabled.
The full .NET regression suite passes **1,132 tests**, including real integration/security tests;
`dotnet format --verify-no-changes --severity warn --no-restore` passes. The initial stdio run
also exposed the old fixed-sleep probe closing input as a refusal completed. The probe now waits
for initialize and tool replies; its refusal assertions are unchanged. Initial failure evidence
is retained in `out/npx-real-stack-initial-20261025`.

**Production native artifacts:** the unchanged tarball hash
`62d1ebcab798e227e0c514f88bc28a38df30013e84b9d883209fccd8977fcfda` now also passes installed
native-store/.NET interchange on Windows x64, Windows ARM64 and macOS ARM64 using Node 24.22.1.
Each drill uses a random synthetic workspace and removes its credential. Reports:

- Windows x64: npm-project `out/package-smoke-tSGpkl/result.json`; installed Koffi hash
  `f7af4982457657d2e7ad46acb076614ed7122d6efbdbfbc75f21ec12681fa121`.
- Mac desktop Keychain: local retained
  `out/npx-remote-evidence/production-client/native-mac-tavP02/{package.json,inventory.json}`;
  Koffi hash `9e4814a0593d95cd3fa1531fe5f06a9908a7505caefd8ccdce2ca758cdf0b5dc`.
  A temporary GUI-user LaunchAgent allowed normal OS interaction and was removed afterward.
- Windows ARM desktop: local retained
  `out/npx-remote-evidence/production-client/native-evidence-b5571ec66edb4390b67ca2b49e16a8d5/`.
  Koffi is NotSigned, hash `902e5e1a5d949b8ff361ffd5897f87863e3506747ac701875250988cf930bdf3`.
  The temporary scheduled task ran at Limited level and was removed. Read-only snapshots show
  active SAC before and after; independent UTC verification proves they bracket the test.

The Windows collector's first bracket boolean was wrong because a PowerShell DateTime cast
converted ISO Z strings to local time before comparing with UTC values. Original reports are
preserved. `tools/verify-native-evidence.mjs` independently verifies the original ISO timestamps,
active policies, native success and hashes, and writes a separate
`independent-policy-verification.json`. Its valid-offset and nine invalid-evidence mutations pass.
The collector is corrected for subsequent runs. The earlier Windows x64 non-admin policy read
failed with Access Denied; the owner-run administrator collector subsequently closed this gap
with the actual limited-user production test (see the x64 acceptance below).

**Host connection smoke:** local Codex CLI 0.160.0 app-server and Claude Code 2.1.259 both
initialize and discover tools through the production header helper, with isolated app homes,
synthetic MCP data, absolute paths/spaces/Thai path and no model turn. Final reports are npm-project
`out/host-codex-nEQ90E/result.json` and `out/host-claude-TErSsq/result.json`. Codex filters the
custom test-home variables from its helper; a clearly identified test wrapper supplies only the
synthetic registry/store before importing the production CLI. This does not bypass origin checks
or demonstrate that arbitrary environment overrides reach a real user's helper. Claude also
passed an earlier direct-entry helper check (`out/host-claude-vkEUUd`).

These host fixtures prove connection/discovery, not full GUI/model/Cowork sessions against the
real stack. `host-smoke.mjs` uses temporary `CODEX_HOME`/`CLAUDE_CONFIG_DIR`; shipped configs and
the user's existing host settings were not edited. Sources for the test protocol/settings:
[official OpenAI MCP docs](https://learn.chatgpt.com/docs/extend/mcp),
[official app-server docs](https://learn.chatgpt.com/docs/app-server), and
[Claude Code helper docs](https://code.claude.com/docs/en/mcp#dynamic-headers).

Current local npm checks: **41 Bun tests, 282 assertions**, strict production/test typechecks
pass. Open gates include full three-host GUI/real-stack
acceptance, final release security patch/dependency review, GitHub CI, license/publisher rights
and owner-approved publication. No npm publication or plugin-default switch occurred.
## Real BMS registration/credential verification on Mac — 2026-10-25

### Claude Desktop live BMS read-only acceptance

After enabling `--use-system-ca` on the opt-in `devbuddy-npx-bms-test` entry and restarting
Claude Desktop, the owner supplied a screenshot of the completed real-host test: Claude
reports `list_projects` succeeded once, returned three projects, and performed no writes.
The UI shows "Used 2 tools"; the screenshot does not independently identify both invocations,
so exact call count/no-write claims remain host-reported, not an audited server-side count.
Dedicated server log corroborates successful initialize at 2026-10-25T11:35:36.251Z and
tools/list at 11:35:36.318Z, following the earlier certificate failure. No token or returned
project content was collected. This closes the basic Claude Desktop real-server discovery
and owner-observed read-only tool-call check. Cowork/Claude Code-specific sessions and the
remaining token/error/rollback acceptance cases are still separate outstanding checks.

### macOS app-security scope clarification — 2026-10-25

Owner clarified the requested fresh check concerns macOS app blocking, not a new registry or
workspace. Read-only current-user checks report `spctl --status`: assessments enabled. Tested
Node has Node.js Foundation Developer ID signing (HX7739G8FX), but no
`com.apple.quarantine` attribute. Explicit `spctl --assess --type execute` returns rejected:
"the code is valid but does not seem to be an app"; do not label this a successful Gatekeeper
assessment of the CLI executable. Prior runtime/Keychain/host success does not establish clean
download/quarantine/notarization acceptance or absence of historical per-file exceptions.
No Gatekeeper policy, quarantine attribute, trust exception, registry or credential was changed
by these checks. A separate distribution-path assessment remains outstanding; current native
runtime success must not be presented as equivalent to the Windows SAC enforcement drill.

### Claude Code (Desktop Code tab) live BMS acceptance — 2026-10-25

In response to the requested Code-tab test, the owner supplied a screenshot showing tool
discovery selecting `mcp__devbuddy-npx-bms-test__list_projects`, followed by that integration's
List projects invocation. Result reports success and three projects. UI's two tools comprise
discovery and the project-list invocation; no additional list call is visible in the screenshot.
The host reports no shell/file access or writes; those claims were not independently reconciled
against server/OS audit. No returned project names/IDs are retained in this record. Basic
Claude Code Desktop real-server discovery/read-only invocation is owner-observed accepted;
standalone macOS CLI is not covered. Existing unrelated integration remained present and the
host reports it was not used. Broader lifecycle/error/rollback and release checks remain open.

### Codex live BMS read-only acceptance — 2026-10-25

Owner supplied a screenshot of the new Codex session after configuration of the dedicated
`devbuddy-npx-bms-test` stdio entry. The prompt explicitly required this server (no fallback
to existing `devbuddy`), one `list_projects` call, no shell/file reads, and no writes.
The host reports success and three projects. Record basic real-server tool invocation as
owner-observed; the collapsed screenshot does not independently establish tool identity,
exact call count or absence of other actions, and no server audit reconciliation was done.
Configured launcher is the isolated Node with `--use-system-ca`, production npm bridge and
explicit BMS path; config verification/backup is logged in Phase 14. No token/project payload
was collected. Broader host lifecycle/error/rollback and Claude Code acceptance remain open.

### Cowork live BMS read-only acceptance

The owner supplied a second screenshot and explicitly confirmed it was from Cowork.
It identifies the `devbuddy-npx-bms-test` integration and reports successful `list_projects`,
three projects, and read-only behavior. Record this as owner-observed Cowork real-server
discovery/tool-call acceptance using the opt-in npm bridge with existing Mac Keychain
credentials and system CA trust. No returned project details or token were collected.
The exact call count and absence of writes are host-reported, not independently reconciled
with server audit. This closes the basic Cowork connection/read-only invocation check;
it does not close broader lifecycle/error/rollback, Claude Code, or release acceptance.

Owner ran `tools/npx-feasibility/check-bms-mac.command` from Mac Terminal because SSH
cannot access Desktop under macOS privacy controls. BMS, budgetapplication-backend and
budgetapplication-frontend each passed production npm `show --json` and `doctor`, using their
existing registration and credential against the existing server. All six command exit codes
and the overall exit code are zero; the script checked the registry hash remained unchanged.
No project file, registration or token update was requested. Evidence retained in ignored
`out/npx-remote-evidence/production-client/bms-readonly-b78MZI/` and the original Mac directory.
This verifies real registration selection and authenticated workspace/endpoint checks, not
host/model tool invocation, GUI/Cowork acceptance, or a synthetic fixture.

## Windows x64 production SAC acceptance — 2026-10-25

The owner ran the administrator dispatcher; its actual native test ran as the Limited desktop
user. Independent verification passed: active SAC snapshots bracket the successful installed
native/.NET exchange, local/global install and cold/warm-offline npx run. UTC times: before
11:22:31.7725844Z, start 11:22:32.4272517Z, finish 11:22:42.5491749Z, after
11:22:42.6323504Z. Node 24.22.1 x64 and artifact SHA-256
`62d1ebcab798e227e0c514f88bc28a38df30013e84b9d883209fccd8977fcfda` match prior acceptance.
Koffi remains NotSigned, hash
`f7af4982457657d2e7ad46acb076614ed7122d6efbdbfbc75f21ec12681fa121`.
This closes the x64 production SAC evidence gate for this machine/artifact, not a general
unsigned-module compatibility guarantee. No SAC setting was changed by the dispatcher.

Original evidence remains at repository-root
`native-evidence-c3ca369da7e942a283598393d716766d`. An unchanged copy and separate independent
verification are retained in ignored
`out/npx-remote-evidence/production-client/native-evidence-c3ca369da7e942a283598393d716766d/`.

## Dependency lifecycle acceptance — 2026-10-25

Windows x64 Node 24.22.1 passed `tools/install-lifecycle-smoke.mjs`: packed production
artifact installed with npm lifecycle scripts enabled, in a Thai/spaced directory, with
cmake/cl/cc/gcc/g++/clang/ninja/make unavailable on the child PATH. Koffi 3.3.3's actual
install hook ran and its prebuilt native module loaded; no source rebuild was reported.
Artifact SHA-256 remained `62d1ebcab798e227e0c514f88bc28a38df30013e84b9d883209fccd8977fcfda`.
Evidence: ignored `src/clients/devbuddy-npx/out/install-lifecycle-cVe21v/{result.json,install.log}`.

Reviewed dependency code: `koffi/package.json` invokes cnoke with `--prebuild`; cnoke's
`checkPrebuild` launches Node to load Koffi, returning before CMake when successful.
If loading fails, it can invoke CMake and a compiler. This dependency installation process
is distinct from DevBuddy production execution, which remains guarded against process APIs.
Script-disabled acceptance remains separately tested. This run covers Windows x64 only;
it does not establish lifecycle-enabled install behavior on every platform or SAC enforcement.

