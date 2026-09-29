# Phase 14 — Making releases repeatable, and search measurable

**Status: APPROVED 2026-09-24** "as recommended" (`info.md`, same day). This file is the plan of
record for Phase 14 and its progress log, as `docs/plan-phase-13.md` is for Phase 13. B4 is
answered by its recommendation. B1, B2 and B3 still wait on the owner, and nothing in them starts
until each answer is recorded in `info.md`.

**Rule for this file:** when a work item is finished, its status line changes and a dated entry
goes into the [Progress log](#progress-log) at the bottom. The entry says what was verified, where,
and what was not. An item is `DONE` only when its exit criterion is met. `IMPLEMENTED` is not
`TESTED`, and a build that was not run is reported as not run.

Status values: `TODO`, `IN PROGRESS`, `BLOCKED (reason)`, `DONE`, `CLOSED — NOT POSSIBLE (reason)`.

## Where this came from

On 2026-09-24, after `v1.6.0`, these were still open:

- **Phase 13 C8:** the hosted mode against a real server. It is blocked because no hosted server
  exists (`info.md`, 2026-09-23).
- **Deferred by the owner:** HTTPS for the devbox (`info.md`, 2026-09-17).
- **Accepted and stated in every release's notes:**
  - Firefox sometimes loses the first click on a signed-out form, cause not identified.
  - The Windows archives are not code-signed.
  - No published image has run on server-class arm64 hardware.
- **Known from running releases:**
  - The release checklist lives in scripts copied by hand between releases on three machines, and
    one of them has a counting bug.
  - A plugin session's MCP container keeps running the old image after an upgrade.
- **Known from embeddings:**
  - DevBuddy sends no query instruction to Qwen3-Embedding, so retrieval sits below the model's
    published figure (`info.md`, 2026-09-16).
  - Nothing measures whether semantic search finds the right record, so no setting can be
    compared with another.

The groups follow Phase 13's:
- **A:** work that can start at once.
- **B:** decisions waiting on the owner.
- **C:** search quality and test gaps.
- **D:** what carries over.

## Out of scope unless the owner says otherwise

- **A generative model.** It needs an ADR first (`info.md`, 2026-09-21).
- **Automatic failover between model servers.** The LM Studio standby on JMPC is switched to by an
  `info.md` entry, not by code (`info.md`, 2026-09-24).
- **Growing the AI surface.** It stays at twenty operations.

## Guiding constraints (unchanged)

These come from `info.md`, `CLAUDE.md` and `AGENTS.md`, and nothing in this phase relaxes them:

- `CR` is banned.
- No SQLite.
- Warnings are errors.
- Images are chiseled.
- Analysis never executes.
- AI access is denied by default.
- Whether an AI wrote something comes from the channel.
- `operations.ts` is generated, never hand-edited.
- The embedding mode follows where the model server is: `SelfHosted` for the stack or the LAN,
  `HostedApi` for anything reached over the internet (`info.md`, 2026-09-23).

## Order of work

```
14.1  A1 release checklist in the repo        (no product code)
14.2  A3 stale MCP containers after upgrade    (docs, and a check)
14.3  C3 Firefox first click                   (investigation first)
14.4  C1 query instruction  +  C2 retrieval evaluation   (C2 first, so C1 is measured)
14.5  A2 automated release rows                (after B4)
14.6  B items the owner approves               (B1 HTTPS, B2 signing, B3 hosted server)
14.7  release v1.7.0, devbox to that tag
14.8  C4 ZAP scan in CI, report-only         (asked for after 14.7, 2026-09-25)
14.9  release v1.8.0, devbox to that tag     (asked for 2026-09-26)
```

A1 comes first because every later release runs the checklist, and the next one would otherwise be
copied by `sed` again. C2 comes before C1 because a change to retrieval that nothing measures is a
guess.

---

## A — Work that can start at once

### A1 — The release checklist, in the repository
**Status: DONE 2026-09-25.** `v1.7.0`'s checklist ran from `tools/release/` at the tag, on every
machine, and `release-matrix.md` names the script behind each row.

- **Problem:** the checklist scripts live on jmhp, the Ubuntu arm64 guest, the Mac mini and the
  Windows on ARM guest. Each release copies the last one's with `sed`. Part 2 prints "AI
  operations: 0" because its pattern does not match the console's columns. Nobody but this
  project's history knows where they are.
- **Work:**
  - Move them to `tools/release/`, parameterised by the version and the previous version:
    - `setup-and-suite.sh`;
    - `stack-drill-tokens.sh`;
    - `upgrade.sh`, for amd64 and arm64 both;
    - `post-images.sh`;
    - `smoke.sh`;
    - `client-smoke.ps1`, for `win-arm64`.
  - Fix the operations count by using the console's own `ai` column.
  - Write the new checks of a release as a list in the script, so a release adds lines and does not
    edit the logic.
  - Secrets are generated at run time and never printed, as now. Nothing in `tools/release/` may
    hold one.
- **Not done:** running a script on a machine without the owner's standing permission for it.
  jmhp has that permission for Linux testing.
- **Exit:** the `v1.7.0` checklist runs from `tools/release/` at the tag, on every machine, and
  `release-matrix.md` names the script behind each row.
- **Built 2026-09-24:** six scripts and `lib.sh` in `tools/release/`, with a README. Each check
  prints `ok` or `FAIL` against an expected value, and each script exits non-zero if any check
  failed, which A2 needs. `DeploymentTests.no_release_checklist_script_carries_a_secret` guards the
  directory. `release-matrix.md` maps each checklist step to its script. **Still owed for the exit:**
  the `v1.7.0` run, from the tag, on every machine.

### A2 — The release rows a runner can do
**Status: DONE 2026-09-28.** `v1.10.1`'s draft carried *Runner smoke results*, every row passing,
from release run 36375258645.

- **What GitHub's hosted runners can run:**
  - `windows-11-arm`: the `win-arm64` client smoke;
  - `ubuntu-24.04-arm`: the `linux-arm64` and `linux-musl-arm64` smoke, and the published images
    started on server-class arm64;
  - `macos-15`: the `osx-arm64` smoke, on Apple silicon.
- **Work:** a job in `release.yml`, after the images are pushed, that downloads the draft's own
  archives and runs the `tools/release/` smoke scripts on each runner. The job fails the release if
  any row fails.
- **What stays by hand:**
  - the destroy-and-restore drill;
  - the upgrade from the previous release;
  - publishing.
- **Exit:** a release's draft carries the smoke results, and the rows B4 accepts are no longer run
  by hand.

### A3 — A plugin session keeps the old image after an upgrade
**Status: DONE 2026-09-25.** The devbox's move to `v1.7.0` left two sessions on the old image,
and `stale-sessions.sh` listed both.

- **Problem:** `docker compose run mcp --stdio` starts a container per session. After an upgrade,
  a session that was open keeps the container, and so the image, it started with. On 2026-09-24
  three were still on the old image hours after the devbox moved to `v1.6.0`.
- **Work:**
  - `docs/operations/deployment.md` and `plugin-hosts.md` say to restart assistant sessions after
    an upgrade.
  - The upgrade checklist lists `mcp-run` containers older than the upgrade.
  - Decide whether the stdio wrapper on the devbox should refuse to reuse an old image. It does
    not reuse one today, so this may be documentation only.
- **Exit:** the upgrade section says it, and the next devbox upgrade records how many old sessions
  it found.
- **Decided 2026-09-24: documentation and a listing, no wrapper change.** The wrapper never reuses
  an image: every `compose run` takes the service's current one. On the devbox that day, all four
  `mcp-run` containers still had a live SSH session and `compose run` above them, so they were open
  assistant sessions, not orphans. The three from before the `v1.6.0` upgrade were on the old image.
  Refusing an old image would mean killing an open session, which is what restarting it does
  anyway, more cleanly.

### A4 — MCP over HTTPS with a machine token, and the `devbuddy` client

**Status: IN PROGRESS.** Decided by the owner on 2026-09-27 (`info.md`). Written and tested on
the Windows development machine the same day: the server half, the `devbuddy` client (ADR-0015),
both plugin packages, the release packaging and the gateway route in `tools/devbox/gateway`.
Not yet done: the Mac mini trial, the e2e run, a release, and the devbox.

- **Problem:** the plugins reach the devbox over SSH to a stdio wrapper that reads one token from
  the server. Every key runs as that token, a second user needs an administrator to place theirs,
  and SSH is unreachable over the VPN. The HTTP transport exists, but it accepts only the web
  client's access token: fifteen minutes long, and not scoped to a workspace.
- **Work:**
  - **Done 2026-09-27:** the MCP server's HTTP transport takes a machine token as its bearer and
    nothing else, resolved on every request and scoped to its workspace exactly as stdio is,
    mapped at `/mcp`, and rate-limited per person. ADR-0006 is amended rather than a new ADR
    written for this half.
  - **Written 2026-09-27:** the gateway passes `/mcp` to the `mcp` service
    (`tools/devbox/gateway/Caddyfile`, validated with `caddy validate`, not deployed). `compose run`
    containers join the network without the service alias unless run with `--use-aliases`; check
    that on the devbox when it is deployed.
  - **Written 2026-09-27:** ADR-0015 and the client, `src/clients/DevBuddy.Client`, published in
    every archive under `Client/`, with its own SBOM and a smoke check in both smoke scripts.
  - **Written 2026-09-27:** both plugin packages connect over HTTP through the helper and hold no
    token or server secret; `plugin-hosts.md` describes the install, with stdio as the
    administrator's route. Still to do: the threat model.
- **Try first, on the Mac mini, before writing server code:** does Codex's `http_headers_helper`,
  and Claude Code's `headersHelper`, run in the session's working directory, and how often; and
  does Codex trust the gateway's CA through `CODEX_CA_CERTIFICATE`. That needs a temporary `/mcp`
  route on the devbox's gateway, which the owner approves first.
- **Decided 2026-09-27:** machine tokens instead of access tokens; a per-person limit, with
  unauthenticated requests not counted.
- **Exit:** Codex on the Mac mini and Claude Code on the Windows machine both work in one
  registered checkout over HTTPS with one token; an unregistered checkout is refused with the
  message; a revoked token is refused on the next call; the tests for the workspace ceiling over
  HTTP pass and are mutation-checked.

---

## B — Decisions waiting on the owner

Each item says what it would cost. None is started without an answer recorded in `info.md`.

### B1 — HTTPS for the devbox
**Status: DONE 2026-09-26.** The owner chose option 3 (`info.md`): no domain, an internal CA, and
Caddy in LXC 100. The gateway runs, the LAN's plain HTTP port is closed, every device the owner
uses trusts the root, and HSTS is sent. **A browser does not record HSTS for an IP address** (RFC
6797, 8.1.1), so the header takes effect only if the devbox is later reached by a name.

- **Problem:** the devbox serves the web UI and the API on the LAN over plain HTTP, and sign-in
  passwords cross it. It has been LAN-only since 2026-09-14, when the router forwards were removed.
- **Options raised on 2026-09-14:**
  1. **Tailscale or ZeroTier**, which the box already has. It needs no certificate and adds no
     public exposure. It only reaches enrolled devices.
  2. **An owned domain, with Cloudflare DNS-01 behind Caddy.** It gives a real certificate with no
     inbound port, and it costs a domain.
  3. **An internal CA.** It works everywhere on the LAN, and every device has to trust it.
- **Recommendation:** option 1 if only the owner's devices use it, and option 2 if anyone else
  will.

### B2 — Code-signing the Windows archives
**Status: DONE 2026-09-27.** The owner chose option 3 for now (`info.md`): stay unsigned, and keep
stating it in every release's notes.

- **Problem:** Smart App Control refuses a new unsigned build, which the development machine has
  already hit. Every release's notes say the archives are unsigned.
- **Options:**
  1. **Azure Trusted Signing**, which costs a monthly fee and needs an identity validation.
  2. **An OV code-signing certificate**, bought yearly.
  3. **Stay unsigned**, and keep stating it.
- **If signed:** `release.yml` signs the `.exe` apphosts for `win-x64` and `win-arm64` before the
  archive and its attestation are made. A test in the checklist checks the signature.

### B3 — A hosted model server of the owner's own (Phase 13 C8)
**Status: DONE 2026-09-27.** The owner's answer (`info.md`): prepare one, but do not use it yet.
`tools/hosted-model/` is that server, tested on devrelease only. No installation points at it.

- **Question:** should there be an Ollama or LM Studio on a cloud machine at all? If there should,
  it runs behind a TLS proxy that checks a key (`deployment.md`), and C8 runs against it with
  synthetic data.
- **If not:** C8 becomes `CLOSED — NOT POSSIBLE (no hosted server is wanted)`. The hosted mode
  stays in the code, tested against a stand-in.

### B4 — Do runner-run smoke tests count for the verified tier?
**Status: DONE 2026-09-24.** The runners replace the hand smoke tests once A2 exists, and the Mac
mini still runs `osx-arm64` for a release that changes the macOS build (`info.md`).

- **Question:** do A2's runner results replace the hand runs for `win-arm64`, `linux-arm64`,
  `linux-musl-arm64` and `osx-arm64`? Or do they add to them?
- **Trade-off:** replacing them makes a release hours shorter. The hand runs are on the owner's own
  machines and a runner is not. The Mac mini and the VMware guests are Apple M4, while the runners
  are server hardware, which is the gap the notes state.
- **Recommendation:** the runners replace the hand smoke tests. The Mac mini still runs
  `osx-arm64` for a release that changes the macOS build.

---

## C — Search quality and test gaps

### C1 — A query instruction for the embedding model
**Status: DONE 2026-09-25.** Built, tested and measured, and it helps. `v1.7.0` carries it, and
the devbox ran Qwen3's published instruction (`info.md`) until the owner reinstalled that machine
the same day. Its LXC successor runs no embedding provider, so nothing uses the instruction there.

- **Problem:** Qwen3-Embedding is trained to receive a query as
  `Instruct: <task>\nQuery: <text>` and a document as plain text. DevBuddy sends both plain, which
  `info.md` records as costing retrieval quality.
- **Work:**
  - Add `Embedding:QueryInstruction`, empty by default.
    - When it is set, `search_similar_records` sends the query with that instruction.
    - The sweep sends documents as they are.
    - The port gains back the purpose that left with Voyage, as a vendor-neutral argument.
  - The instruction is operator configuration, not request data, so it is not audited as content.
  - **SB-17 scans the text as sent**, instruction included, in the gateway as now.
  - It changes query vectors only, so nothing is re-embedded.
- **Tests:**
  - the query carries the instruction, and documents never do;
  - an empty setting sends exactly what it sends today;
  - the gateway still refuses a secret in the query.
- **Exit:** C2's evaluation shows whether it helps. It is set on the devbox only if it does, and
  that change is recorded in `info.md`.

### C2 — Measuring retrieval
**Status: DONE 2026-09-25.** Ollama and LM Studio are both measured and recorded. The
comparison with a query instruction is C1's exit, and is measured with this tool.

- **Problem:** nothing measures whether semantic search finds the right record. Neither
  C1, chunk size nor a model change can be judged.
- **Work:**
  - A fixed evaluation set of synthetic records and queries, in Thai and in English, each query
    labelled with the record it should find. No project data goes into it.
  - A console command or test tool that loads the set into a throwaway pgvector stack, embeds it
    with a named model server, and reports recall@1, recall@5 and MRR.
  - Run it against the devbox's Ollama and the LM Studio standby, which should agree, and with and
    without C1.
  - The CI stand-in embedder is a bag of words, so these numbers come from a real model and not
    from CI.
- **Exit:** the numbers are recorded in `docs/operations/embedding-approval.md`, with the model, the
  chunk size and the instruction they came from.

### C3 — Firefox loses the first click on a signed-out form
**Status: CLOSED — NOT POSSIBLE (the press never reaches the page, so there is nothing in the
client to fix).** 2026-09-24. `clickUntilSent` stays.

- **Known:**
  - In CI, Firefox sometimes sends nothing on the first submit of a freshly loaded signed-out page,
    on both runners.
  - `clickUntilSent` in `tests/e2e/support/fixtures.ts` repeats the click, so the suite passes.
  - The cause is not identified, and it is not known whether a person clicking by hand is ever
    affected (plan log, 2026-09-22).
- **Work:**
  1. Reproduce it with the retry switched off and Playwright tracing on, many runs, on jmhp and in
     CI.
  2. Test the likely causes:
     - the form rendered before its handler is attached;
     - a submit during the first `/me` check;
     - a focus or autofill event swallowing the click.
  3. Fix it in the client, not in the tests, as C7 required.
- **Exit:** either the cause is fixed and `clickUntilSent` goes back to a plain click, with CI
  green twice, or the item is `CLOSED — NOT POSSIBLE` with what was ruled out.
- **Found (2026-09-24):**
  - **The instrument.** With `DEVBUDDY_E2E_FIRST_CLICK` set, every page in the suite records the
    input events it receives, on `window` in the capture phase, ahead of anything the client
    does. `clickUntilSent` prints that record for every click that sent nothing.
  - **Where it is lost.** Six CI runs of the diagnostic branch `c3-first-click` caught three lost
    clicks: 35998589217 on the recovery form, then 36001555287 and 36001564458 on the sign-in form.
    All three were Firefox on the arm64 runner, and all three have the same record: the button
    received `mouseup` and never `pointerdown`, `mousedown` or `click`. The press was dropped before
    it reached the document.
  - **How often.** On the arm64 runner, about 348 clicks lost 3; on the x64 runner, about 348 lost
    none. On jmhp none of 922 were lost, across every configuration below.
- **Ruled out:**
  - **The form rendering before its handler is attached.** The page never received the press, and
    before clicking, the diagnostic saw React's handlers on the form and the button.
  - **A submit during the first `/me` check.** A signed-out page makes none, and the record shows
    no request in flight.
  - **The client at all.** No code in `web/admin` runs before a capture listener on `window`, so
    nothing there can drop an event that listener never saw.
  - **Firefox's insecure-login warning, and its form-history dropdown.** Either would take a press
    to close itself, which fits the record. Neither reproduced: 40 sign-ins and 120 recoveries
    with a 1.5 s pause for a dropdown to open, with one worker so form history built up, and with
    the warning switched off as a control. All were sent.
  - **Load on x64.** None was lost on jmhp: 200 single clicks, the full Firefox suite three times
    with 8 workers, and the full suite in all three browsers twice with 12 workers. None was lost
    on the x64 runner either.
- **Left open:** whether the press is dropped by Firefox or by Playwright's Firefox driver on that
  runner. Nothing a page can observe tells the two apart. Whether a person clicking by hand in
  Firefox is ever affected is still unanswered, and only someone with Firefox can answer it.

### C4 — An OWASP ZAP scan in CI
**Status: DONE 2026-09-26.** Asked for and approved on 2026-09-25 (`info.md`). The findings were
triaged, the fixes merged, and `tests/e2e/zap/rules.tsv` now decides whether a run passes.

- **Problem:** nothing in CI looks at the running system the way an outside scanner would. That
  means headers, error pages, and how each route answers a caller it should refuse.
- **Work:**
  - `DEVBUDDY_E2E_ZAP=1` in `tests/e2e/run.sh` runs ZAP's baseline scan and its API scan from
    `/openapi/v1.json` after the suite, against the same stack. ZAP is a `zap` service pinned by
    digest in `compose.e2e.yaml`.
  - Report-only at first. The reports go to `zap/` under the output directory, and on the amd64
    run to the job summary. Since the rules file, the scans fail the run; see below.
  - At no cost. ZAP is open source, and it runs on the same GitHub-hosted runners, which a public
    repository does not pay for.
- **Fixed from the first findings,** at the owner's word the same day:
  - A NUL character in any text answers 400, not PostgreSQL's 500. `TextInput` checks every
    operation's arguments in the dispatcher, the `/auth` routes through an endpoint filter, and the
    evidence form in its handler.
  - A duplicate work item key answers 409 with the key in the reason. The repository translates the
    unique-index violation and detaches the row, so the audit entry for the attempt is written.
  - Every answer from the API host carries a content security policy, `frame-ancestors 'none'`,
    `X-Frame-Options`, `nosniff`, a referrer policy, a permissions policy and CORP. COOP and COEP are
    sent only when the page arrived over HTTPS, because over plain HTTP Chromium logs an error for
    COOP on every page; the e2e suite found that.
  - The e2e run keeps the servers' logs from before its restore stage.
- **Exit:** the first findings are triaged with the owner, and the accepted ones are written into a
  rules file, so that a new finding at the level the owner chooses fails a run.
- **Later, if the owner says so:** a weekly full active scan on the supply-chain schedule, and an
  authenticated scan.

### C5 — Secret, source and image scans, and every action pinned
**Status: DONE 2026-09-27.** Supply chain and CodeQL pass on `main`, and C6 removed the MinIO
findings the image gate had accepted. Approved 2026-09-26 (`info.md`), from a comparison with the HomeHub rule
set in `samples/`, which requires Gitleaks, a SAST tool, Trivy, actions pinned by commit and a
controlled update process. This repository had none of the five.

- **Gitleaks** over the whole history, in `supply-chain.yml`. A finding is accepted in
  `.gitleaksignore` by exact fingerprint only.
- **Trivy** over the repository (lockfiles, Dockerfiles, Compose) and over the four built images.
  The gate is HIGH and CRITICAL with a fix available. A finding is accepted in `.trivyignore.yaml`
  with a statement and an expiry, and Trivy stops honouring an expired entry.
- **CodeQL** for C#, TypeScript and the workflows, `security-extended`, in `codeql.yml`.
- **Every action pinned by commit**, with its version in a comment, and both scanner images by
  digest.
- **Dependabot** for actions, NuGet, Bun and base images, weekly, grouped. It merges nothing.
- **`SupplyChainPolicyTests`** holds the four rules above them: actions by commit, scanner images by
  digest, Gitleaks by fingerprint, Trivy entries with a statement, an expiry and no wildcard.
- **Exit:** all three workflows pass on `main`, and whatever the image scan finds is fixed or
  accepted in `.trivyignore.yaml` at the owner's word.

### C6 — Replacing MinIO
**Status: DONE 2026-09-27.** `v1.9.0` shipped SeaweedFS with no MinIO entry left in
`.trivyignore.yaml`, and `upgrade.sh` restored a MinIO installation's evidence into it, 1 of 1, on
amd64 and arm64. The devbox moved the same day. Planned at the owner's word (`info.md`, 2026-09-26). **ADR-0014 proposes
SeaweedFS and waits on the owner.** The SeaweedFS spike is done and passed (`tools/spikes/c6-seaweedfs/`, and the log). Nothing is built into the
product until the owner chooses a store.

- **Why:** `minio/minio` is archived upstream since 2026-04-24, and `minio/mc` since 2025-11. C5's
  first image scan failed the evidence store. Moving to the last releases and Go 1.26.8 cleared the
  standard library and MinIO's own CVE-2025-62506, but 39 findings in `minio` and 23 in `mc` remain
  in modules MinIO pins. They are accepted in `.trivyignore.yaml` until **2026-12-26**, and nothing
  upstream will fix them. That date is this item's deadline.
- **What DevBuddy needs from a store** (read from `EvidenceStore.cs`, not assumed): `PutObject`,
  `GetObject`, `DeleteObject`, `ListBuckets`, `PutBucket`, over the AWS SDK with path-style
  addressing, and **SSE-S3 (`AES256`)** on every write, which `MINIO_KMS_SECRET_KEY` backs today.
  Plus the project's own rules: built from source pinned by commit, non-root, read-only,
  `scratch` or chiseled, native on amd64 and arm64, and started by `EvidenceStoreTests`.
- **Candidates:**
  1. **The filesystem adapter that already exists** behind `IEvidenceStore`. It needs no second
     service and no third-party code, and the backup already carries the bytes. It loses
     encryption at rest, which would have to be added in the adapter, and the store stops being
     separate from the API's process.
  2. **SeaweedFS** (Apache-2.0, Go). It is maintained, has an S3 gateway, and has supported
     SSE-S3 since 2025. It is several components where MinIO was one.
  3. **Garage** (AGPL-3.0, Rust). It is small, a single static binary, and maintained. **It supports
     SSE-C only, not SSE-S3**, so the adapter would change what it sends, and the key would move
     into the API.
  4. **RustFS** (Apache-2.0, Rust). It is built as a MinIO replacement, but it is young. It needs
     its maintenance, SSE-S3 support and release cadence checked before it counts.
- **How existing data moves:** a backup carries every evidence object. Restoring one into a stack
  with the new store is the migration, and the restore drill in `tools/release/` is its test. No
  in-place conversion.
- **Work once the owner chooses:** an ADR replacing ADR-0004. Then the Dockerfile, Compose,
  `EvidenceStoreTests` and `DeploymentTests` for the new store, the drill run through a
  restore, `deployment.md`'s upgrade step, and removing the MinIO entries from
  `.trivyignore.yaml`.
- **Recommendation, to be confirmed by a spike:** SeaweedFS if SSE-S3 holds up as the adapter
  uses it; otherwise the filesystem adapter with encryption added. Garage's lack of SSE-S3 and
  RustFS's age count against them.
- **Exit:** the evidence store passes the image gate with no MinIO entries accepted, and a backup
  from a MinIO installation restores into it.

---

## D — Carried over

### D1 — Phase 13 C8, the hosted mode against a real server
**Status: BLOCKED (no cloud machine, by the owner's choice).** B3 prepared the server in
`tools/hosted-model/`, and the owner asked for it not to be used yet. C8 runs when the owner puts
it on a cloud machine and accepts that, with synthetic data.

---

## 14.7 — Cutting v1.7.0
**Status: DONE 2026-09-25.** Proposed and approved as proposed the same day (`info.md`). Published
at 13:39 UTC, and the devbox runs the tag.

- **What it carries since `v1.6.0`:**
  - **C1**, the optional query instruction (`DEVBUDDY_EMBEDDING_QUERY_INSTRUCTION`), empty by
    default, so an installation that does not set it sends exactly what `v1.6.0` sent;
  - **the evidence store built from MinIO's source**, pinned by commit (`info.md`, 2026-09-25);
  - **A1 and A3**: `tools/release/`, `stale-sessions.sh`, and the upgrade step telling an operator
    to restart plugin sessions;
  - **C2**'s evaluation tool, which ships in the source only.
- **A minor version.** It adds a setting, and it changes how the evidence store is obtained. No
  migration, so the count stays at nine and nobody signs in again. The Claude plugin moves to 1.7.0
  with its content unchanged, to stay in step, as for `v1.6.0`.
- **`release.yml` is unchanged since `v1.4.0`**, so no throwaway prerelease tag is needed.
- **The checklist runs from `tools/release/` at the release commit**, which is A1's exit:
  - on jmhp: `setup-and-suite.sh`, `stack-drill-tokens.sh`, and `upgrade.sh` from `v1.6.0`;
  - on arm64: `upgrade.sh` in the Ubuntu guest, which the owner starts. **It can only build
    `v1.6.0`'s evidence store if the guest still holds `quay.io/minio/minio` for arm64.** If it does
    not, that row cannot run as written and needs the owner's decision;
  - after the tag: `post-images.sh`, and `smoke.sh` or `client-smoke.ps1` per archive. These owe
    `win-arm64`, `osx-arm64`, `linux-arm64` and `linux-musl-arm64` smoke tests by hand, because A2
    does not exist yet. Downloading an archive and running on a machine other than jmhp each need
    the owner's approval.
- **New checks** (`stack-drill-tokens.sh`):
  - Qwen3's published instruction starts; an instruction with a line break, or of 501 characters,
    is refused at start-up with exit 2;
  - the evidence image's `minio` and `mc` report the pinned releases, it has no shell, and
    `/usr/bin` holds those two binaries only.

  `leftover_dialect` is dropped from `upgrade.sh`, because it described `v1.5.0`. The upgrade's
  existing evidence rows are what prove the source-built store reads a volume the `quay.io` image
  wrote.
- **After publishing:** move the devbox onto the tag with a backup first, set Qwen3's published
  query instruction there (`info.md`, 2026-09-25), record A3's `stale-sessions.sh` count, and
  install plugin 1.7.0.
- **Its release notes must say what a caller will notice:**
  - the first `docker compose build` compiles MinIO and `mc`, which takes three to five minutes and
    needs `golang` from Docker Hub and the two repositories from GitHub;
  - **an installation of `v1.6.0` or earlier cannot be built on a clean machine any more**, because
    quay.io refuses anonymous pulls; upgrading to `v1.7.0` is the fix, and the evidence volume
    carries across unchanged;
  - the query instruction is optional and off by default;
  - after an upgrade, restart assistant sessions, and `tools/release/stale-sessions.sh` lists the
    ones left on the old image;
  - nobody has to sign in again.

## 14.9 — Cutting v1.8.0
**Status: DONE 2026-09-26.** The owner asked for it the same day (`info.md`). Published at 14:20
UTC, and the devbox runs the tag.

- **What it carries since `v1.7.0`:**
  - **C4**: `TextInput` refuses a NUL in any text with 400, a duplicate work item key answers 409,
    and the API sends security headers on every answer, COOP and COEP only over HTTPS;
  - **the email fix**: a mail server that fails is logged and never thrown, so recovery no longer
    tells an address with an account from one without;
  - **the host ports from 5010**: the API on 5010, MCP on 5011 and Grafana on 5012, each a variable
    that sets only the port;
  - in the source only: the ZAP scans in CI, the web suite in CI, and the dev ports.
- **A minor version.** An installation behind a reverse proxy has to move it, so this is not a
  patch. No migration, so the count stays at nine and nobody signs in again. The Claude plugin moves
  to 1.8.0 with its content unchanged, to stay in step.
- **`release.yml` is unchanged since `v1.4.0`**, so no throwaway prerelease tag is needed.
- **The checklist runs from `tools/release/` at the release commit, on devrelease** (LXC 101 on the
  Proxmox host) instead of jmhp, at the owner's choice:
  - on devrelease: `setup-and-suite.sh`, `stack-drill-tokens.sh`, and `upgrade.sh` from `v1.7.0`;
  - on arm64: `upgrade.sh` in the Ubuntu guest, which the owner starts;
  - after the tag: `post-images.sh` on devrelease and on arm64, and `smoke.sh` or
    `client-smoke.ps1` per archive.
- **New checks** (`stack-drill-tokens.sh`): a NUL in the recovery address is 400; the client's
  answer carries the CSP and `nosniff`, and COOP only when `X-Forwarded-Proto` says HTTPS; with the
  SMTP server refusing, recovery answers 202 for an address with an account and one without, the
  failure is logged, and nothing is unhandled; and the release's Compose file publishes
  `127.0.0.1:5010` and `127.0.0.1:5011` by default.
- **After publishing:** move the devbox onto the tag with a backup first. Its override already
  publishes 5010 and 5011, so its address does not change. Record `stale-sessions.sh`'s count, and
  install plugin 1.8.0.
- **Its release notes must say what a caller will notice:**
  - **the ports moved**: a reverse proxy, firewall rule or bookmark naming 8080, 8081 or 3000 stops
    reaching the stack; move it, or set `DEVBUDDY_API_PORT=8080` and `DEVBUDDY_MCP_PORT=8081`;
  - a proxy that sets its own `Content-Security-Policy` or `X-Frame-Options` now sends it twice,
    and should drop its own;
  - a NUL in any text is 400, and a duplicate work item key is 409 naming the key, where both were
    500;
  - recovery answers the same when SMTP fails, and the failure is in the API's log;
  - no migration, nobody signs in again, and assistant sessions should be restarted.

## 14.10 — Cutting v1.9.0
**Status: PUBLISHED 2026-09-27**, at 06:29 UTC, from `59b7dae`, at the owner's instruction
(`info.md`). The devbox runs it since the same day.

- **What it carries since `v1.8.0`:**
  - **C6, ADR-0014**: the evidence store is SeaweedFS, built from source into `scratch` as uid
    1000, on a new volume `evidence_seaweedfs`. Compose requires its credentials and a new variable,
    `DEVBUDDY_EVIDENCE_SSE_KEK`. Its health check fails unless an unsigned request is refused. The
    application refuses a store that answers one, and keeps an SSE canary;
  - **`restore --evidence-only`**: how an installation moves its evidence from MinIO;
  - **the web client's dependencies**: `react-router-dom` 7.18.4 and `vite` 7.3.6, clearing seven
    and three HIGH advisories (C5);
  - in the source only: Gitleaks, Trivy and CodeQL, actions pinned by commit, Dependabot (C5),
    and `tools/devbox/`.
- **A minor version.** The upgrade has steps of its own: a backup first, a new variable, and a
  command after. No migration, so the count stays at nine. Nobody signs in again, because
  the rows stay. The Claude plugin moves to 1.9.0 with its content unchanged.
- **`release.yml` changed since `v1.8.0`**, but only its `uses:` lines, now pinned by commit. The
  release is the first run of those pins. A throwaway `v1.9.0-rc.1` would prove them first, as
  `v1.2.0-rc.1` did for the last workflow change. **The owner decides whether to cut it.**
- **The checklist runs from `tools/release/` at the release commit:**
  - on devrelease: `setup-and-suite.sh`, `stack-drill-tokens.sh` with its new check, and
    `upgrade.sh` from `v1.8.0`. The upgrade takes a backup on `v1.8.0` and runs
    `restore --evidence-only` after it;
  - **on arm64: `upgrade.sh` needs a machine that can build the evidence image.** The Ubuntu guest
    cannot: its disk filled during a SeaweedFS build on 2026-09-27. The Mac mini built it
    natively. **The owner decides:** the Mac mini, or a larger disk for the guest;
  - after the tag: `post-images.sh` on devrelease and on arm64, and `smoke.sh` or
    `client-smoke.ps1` per archive.
- **New check** (`stack-drill-tokens.sh`, `seaweedfs_guards`): on the running stack an unsigned
  request to the store is 403, the API never logged `EvidenceStoreUnsafe`, and
  `restore --evidence-only` over the drill's backup exits 0.
- **After publishing:** move the devbox onto the tag by `deployment.md`'s steps: a backup on
  `v1.8.0`, the new variable, then `restore --evidence-only`. Record `stale-sessions.sh`'s count,
  and install plugin 1.9.0.
- **Its release notes must say what an operator will notice:**
  - **the upgrade is not only `up -d`**: take a backup first, add `DEVBUDDY_EVIDENCE_SSE_KEK`
    (Compose refuses to start without it), then run `restore --evidence-only --reference <ref>`
    after the upgrade. Until then existing evidence cannot be downloaded;
  - **keep the new key somewhere safe**: the new volume cannot be read without it, and a different
    key makes the application refuse the store;
  - MinIO's volume is left as it was, and rolling back is the previous release's Compose file;
  - no migration, nobody signs in again, and assistant sessions should be restarted.

## 14.11 — Cutting v1.10.0
**Status: PUBLISHED 2026-09-27**, at 10:11 UTC, from `de841f8`, at the owner's instruction
(`info.md`, 2026-09-27). The devbox runs it, with plugin 1.10.0.

- **What it carries since `v1.9.0`:** the web client only.
  - the *Ocean Mist Light* theme and a sidebar, from the owner's `demo/` mock-up, with a drawer
    below the `lg` breakpoint;
  - **Thai and English**, switched on every screen and remembered per browser. The server's words
    are not translated. `i18n.test.tsx` holds the dictionary equal to the source;
  - **Roboto and Sarabun**, bundled, and `assetsInlineLimit: 0` so no asset becomes a `data:` URL;
  - **tables that scroll** both ways, with a sticky header and minimum widths.
- **A minor version.** No migration, so the count stays at nine. Nobody signs in again. The
  content security policy is unchanged; only its comment in `SecurityHeaders.cs` changed, because
  the stylesheet now has `url()`s for the fonts. The e2e suite pins `locale: "en-US"`, because the
  client now follows the browser's language. The Claude plugin moves to 1.10.0 with its content
  unchanged.
- **The checklist runs from `tools/release/` at the release commit**, with no new check: on
  devrelease `setup-and-suite.sh`, `stack-drill-tokens.sh` and `upgrade.sh` from `v1.9.0`; on
  arm64 `upgrade.sh` in the Ubuntu guest; after the tag `post-images.sh` on both and `smoke.sh` or
  `client-smoke.ps1` per archive.
- **Its release notes must say:** the upgrade is `up -d` alone, with no step of its own; the screens
  follow the browser's language and a switch at the top changes it; and nothing about the policy,
  the API or the AI surface changed.

## 14.12 — Cutting v1.10.1
**Status: PUBLISHED 2026-09-28**, at 04:11 UTC, from `4082c07`, at the owner's instruction
(`info.md`, 2026-09-27). The devbox runs it since the same morning, moved by the owner.

- **What it carries since `v1.10.0`:**
  - Thai set in **Leelawadee UI** where the device has it, Sarabun where not;
  - Dependabot's updates: the NuGet group within majors, `Serilog.Extensions.Hosting` 10,
    `Microsoft.NET.Test.Sdk` 18 and `coverlet.collector` 10 (tests only), the web client's Bun
    group with React 19.3 and TypeScript 7, and **Go 1.27.1 for the evidence store's build**;
  - A2's jobs in `release.yml`, so **this is A2's first real run**: the draft should gain a
    *Runner smoke results* section.
- **A patch version.** No migration, so the count stays at nine. Nobody signs in again. The
  content security policy is unchanged. The evidence image is rebuilt with the new Go, from the
  same SeaweedFS commit. The Claude plugin moves to 1.10.1 with its content unchanged.
- **The checklist runs from `tools/release/` at the release commit**, with no new check: on
  devrelease `setup-and-suite.sh`, `stack-drill-tokens.sh` and `upgrade.sh` from `v1.10.0`; on
  arm64 `upgrade.sh` in the Ubuntu guest. After the tag the runners do the smoke rows B4 accepts;
  `post-images.sh` and the x64 smoke rows still run by hand as well.
- **Its release notes must say:** the upgrade is `up -d` alone, after pulling the images and
  rebuilding the evidence store; Thai uses Leelawadee UI on Windows and Sarabun elsewhere; and
  nothing about the policy, the API or the AI surface changed.

## 14.13 — Cutting v1.11.0

**Status: PUBLISHED 2026-09-28**, at 10:23 UTC, from `d2fda9b`, at the owner's instruction
(`info.md`, 2026-09-28). The devbox runs it since the same day, moved by the owner.

- **What it carries since `v1.10.1`: A4.** The MCP server's HTTP transport at `/mcp`, taking a
  machine token and nothing else, rate-limited per person; the `devbuddy` client in every archive
  under `Client/`, with its own SBOM and smoke rows; both plugin packages over HTTP through it; the
  Plugin access screen showing the `devbuddy register` command with this server and workspace
  filled in; ADR-0015, ADR-0006's amendment and the threat model's T2.6, T2.7, T3.7 and AL-6.
- **A minor version.** No migration, so the count stays at nine. Nobody signs in again. The
  content security policy is unchanged. The Claude plugin moves to 1.11.0.
- **What an upgrade needs beyond `up -d`:** the reverse proxy passes `/mcp` to the MCP server
  (`deployment.md`); each person installs the client, mints a token and registers their checkouts
  (`plugin-hosts.md`). **On the devbox** the gateway already passes `/mcp`; if its override still
  pins `image: devbuddy-mcp:a4` from the trial, remove that line, or `mcp` stays on that build.
- **Breaking, and the notes must say so:** the HTTP transport refuses the web client's access token
  and answers at `/mcp`, not the root. Anything that reached it the old way stops. Stdio is
  unchanged. The client is unsigned and does not run where Smart App Control is on.
- **The checklist runs from `tools/release/` at the release commit**, as for `v1.10.1`, with the
  client's smoke rows new in both smoke scripts.

## 14.14 — Cutting v1.11.1

**Status: PUBLISHED 2026-09-28**, at 15:23 UTC, from `2075492`, at the owner's instruction
(`info.md`, 2026-09-28). The devbox is not on it yet.

- **What it carries since `v1.11.0`:** `/.well-known/` answers 404 instead of the web client's page
  (PR #33). Claude Code on JMPC, in a checkout not registered with `devbuddy`, got 401 from `/mcp`
  as designed, then asked for OAuth metadata under `/.well-known/`; the fallback answered with
  `index.html` and 200, and Claude Code reported a JSON parse error instead of the refusal.
- **A patch version.** No migration, so the count stays at nine. Nobody signs in again. The content
  security policy, the operations, the MCP transport and the client are unchanged. The Claude plugin
  moves to 1.11.1 with its content unchanged.
- **The upgrade is `up -d` alone**, with the usual check that `mcp` runs the new image.
- **The checklist runs from `tools/release/` at the release commit**, with no new check.

## 14.15 — Cutting v1.12.0

**Status: IN PROGRESS**, at the owner's instruction (`info.md`, 2026-09-29). The pre-tag
checklist passed at `235a17c` (`release-matrix.md`); the tag is the owner's.

- **What it carries since `v1.11.1`: DevBuddy from Cowork** (PR #37 and PR #38).
  - `devbuddy register` takes a folder that is not a git repository; a drive root, the home folder
    and a folder above it are refused. `--label` names a workspace.
  - `devbuddy mcp-bridge`, the local MCP server Cowork starts on the host: it strips the workspace
    from tool schemas and fills it in, uses the one registered workspace without asking, and with
    several holds every call until the person chooses one with `use_workspace`, once per task.
  - `plugins/cowork/`, named `devbuddy-cowork` so a Cowork upload does not replace the Claude
    package in Claude Desktop's Code tab, with a skill written for Cowork.
  - `plugin-hosts.md` on putting the client on `PATH`, and bridge rows in both smoke scripts.
- **A minor version**, because it adds a command and a package. **The server is unchanged:**
  nothing under `src/core`, `src/hosts`, `docker` or `web` changed, so the images, the operations,
  the MCP transport and the content security policy are the same. No migration, so the count stays
  at nine. Nobody signs in again. Both plugins move to 1.12.0. **The Claude package passes the MCP
  URL to its helper** (`--url "${DEVBUDDY_URL}/mcp"`, PR #36, added at the owner's word on
  2026-09-29): Claude Code handed the helper `CLAUDE_CODE_MCP_SERVER_URL` with every `1` redacted on
  JMPC, and the helper rightly refused it. A `v1.11.2` for that alone was planned and dropped.
- **The upgrade is `up -d` alone** on a server, with the usual check that `mcp` runs the new image.
  On a person's machine, the client from the archive replaces the old one; Cowork additionally
  needs it on `PATH`, Claude Desktop restarted, and `plugins/cowork/` uploaded (`plugin-hosts.md`).
- **Not breaking.** The registry gains an optional `label`; an older client reads the file and
  ignores it, but drops it if it saves the registry, so downgrading the client loses labels and
  nothing else. The client is still unsigned and does not run where Smart App Control is on.
- **The checklist runs from `tools/release/` at the release commit**, with the bridge's five rows
  new in both smoke scripts.

## 14.16 — Cutting v1.12.1

**Status: IN PROGRESS**, at the owner's instruction (`info.md`, 2026-09-29).

- **What it carries since `v1.12.0`:**
  - **The Claude Code package passes the MCP URL to its helper** (PR #36), which `v1.12.0`'s does
    not: Claude Code redacts credential values inside `CLAUDE_CODE_MCP_SERVER_URL`, and the helper
    refused the garbled URL. `v1.12.0`'s notes told people not to update that plugin; this one fixes
    it.
  - **WebKit stops hanging on the primary button.** `Button` no longer animates `filter`: animating
    the theme's `hover:brightness-110` while `disabled` toggled opacity stopped WebKit on Linux
    painting the page, 16 of 17 failed e2e jobs since the Ocean Mist theme. And `clickUntilSent`
    counts a button that has gone as a press that went through. Found and fixed in a session of its
    own on 2026-09-28; its log row is below.
- **A patch version.** No migration, so the count stays at nine. Nobody signs in again. The content
  security policy is unchanged. The web client changes, so the API image is rebuilt. Both plugins
  move to 1.12.1.
- **The upgrade is `up -d` alone** on a server, with the usual check that `mcp` runs the new image.
  On a person's machine, update the Claude Code plugin to 1.12.1.
- **The checklist runs from `tools/release/` at the release commit**, with no new check.

## Exit criteria for Phase 14

- Every item is `DONE`, `BLOCKED` with its reason, or `CLOSED — NOT POSSIBLE` with its reason.
- `v1.7.0` is published with its checklist run from `tools/release/`, and the devbox runs that tag.
- The retrieval numbers from C2 are recorded, and the devbox's embedding settings are the ones
  those numbers support.
- `info.md` records each decision this phase took.
- `verification-matrix.md` records each row that gained evidence, and nothing moves backwards.

---

## Progress log

Newest last. Every entry records the date, the item, what was verified and where, and what was not.

| Date | Item | Entry |
| --- | --- | --- |
| 2026-09-24 | Plan | Drafted after `v1.6.0` from the items left open. It waits for the owner's approval and the B answers. |
| 2026-09-24 | Plan, B4 | The owner approved the plan "as recommended" (`info.md`). B4 is answered by its recommendation: the runners replace the hand smoke tests once A2 exists. B1, B2 and B3 carry no single recommendation, so they still wait. A1 started. |
| 2026-09-24 | A1 | **In progress: built, and run on jmhp.** The v1.6.0 copies were collected from jmhp, the Ubuntu arm64 guest and the Windows on ARM guest (the Mac mini had only the v1.4.0 smoke script, which is identical). They were merged into `tools/release/`, parameterised by version, previous version and commit. The amd64 and arm64 upgrade scripts are now one script. The part-2 operations count reads the `ai` column, and the migration count and last migration are read from the checkout. Per-release checks are functions in a list. Run on jmhp from a bundle of the uncommitted change, against published `v1.6.0` as the previous release: **part 1** passed 7 of 7 (the .NET suite **959 passed**, the 958 plus the new guard; format; web 78 of 78; the `linux-x64` publish). **Part 2** passed 92 of 92 on its second run. The first run had 4 failures, all wrong expectations in the script and none in the product: the delivery line goes to standard output, `scope-report --delete` stops at "Nothing was deleted" on a clean installation, and `embedding-check` pads its columns. **Part 3** (upgrade from `v1.6.0` on amd64) passed 40 of 40. **`post-images.sh`** passed 29 of 29 against the published `1.6.0` images, and its twenty names are identical to the v1.6.0 run's. **`smoke.sh`** passed in `ubuntu:24.04` against part 1's own `linux-x64` publish packed as an archive. As a control, it failed with exit 1 in `alpine:3`. The guard test was mutation-checked on jmhp: a literal password planted in `lib.sh` failed it, and the revert passed. **Not run:** `upgrade.sh` on arm64, `post-images.sh` on arm64 or the Mac mini, `smoke.sh` natively on any machine, and `client-smoke.ps1` at all (a PowerShell parse check only). Each needs a machine other than jmhp, or a downloaded archive, and so the owner's approval. The throwaway stacks and volumes were removed; the work directory `/data/devbuddy-cache/a1-verify` is kept. |
| 2026-09-24 | A3 | **In progress: everything except the next devbox upgrade's record.** `tools/release/stale-sessions.sh` lists the stdio session containers (Compose one-off `mcp` containers) whose image differs from the running `mcp` service's. It changes nothing. Run read-only against the devbox's live stack, it listed exactly the three sessions opened before the 05:12 UTC `v1.6.0` upgrade, of four open, all on image `abccc9341eb7`, and exited 2 for a project that does not exist. `deployment.md` gains step 6 of *Upgrading*, and `plugin-hosts.md` gains *After the server is upgraded*. `upgrade.sh` now holds a session open across the upgrade: on jmhp, upgrading from `v1.6.0`, it was listed as stale on `b24b44ff9296` (the published `mcp:1.6.0`), exited 0 when its input closed, and its container was gone afterwards. The script passed 44 of 44. **Not done:** the next devbox upgrade's count. No session on the devbox was stopped. |
| 2026-09-24 | C3 | **CLOSED — NOT POSSIBLE.** Three lost clicks in six CI runs of the diagnostic branch `c3-first-click`, all Firefox on the arm64 runner, all with the same record: the button got `mouseup` and never `pointerdown`, `mousedown` or `click`. The press never reached the page, so nothing in the client can be fixed, and `clickUntilSent` stays with the finding in its comment. jmhp reproduced nothing in 922 clicks, and the x64 runner nothing in about 348. The item above lists what was ruled out. The event recorder stays in the suite behind `DEVBUDDY_E2E_FIRST_CLICK`. The one-off diagnostic spec was not merged. The branch was deleted at the owner's instruction the same day. |
| 2026-09-24 | C2 | **In progress: the tool, the set and the Ollama numbers.** `tools/retrieval/evaluate.sh` and `set.json` (32 synthetic records and 64 questions, English and Thai, in near-neighbour pairs, two long records with their answer past 3000 characters). It runs through the product, from publishing over the HTTP API, through the real sweep as a Viewer, to `search_similar_records` over MCP stdio. Run on jmhp against `qwen3-embedding:0.6b`, the devbox's own model files mounted read-only into an Ollama from the image the devbox pins; the devbox's stack was not touched. **At 3000 characters, no instruction:** recall@1 0.813, recall@5 0.953, MRR 0.883. Thai question to English record is the weak pair (recall@1 0.5, recall@5 0.813), and all three misses outside the top five are that pair. A repeat gave identical ranks. At 1500 characters, three ranks moved by one place, recall was unchanged and MRR was 0.882. Recorded in `embedding-approval.md`. The first run found two faults in the script, both fixed: build output ahead of the JSON, and answers arriving out of order. **Not done:** LM Studio, and with C1. |
| 2026-09-25 | C2 | **DONE.** The owner started LM Studio on JMPC. Its server turned out not to be running, so it was started with `lms server start --port 1234 --bind 0.0.0.0`, and `text-embedding-qwen3-embedding-0.6b` was loaded. The same evaluation from `9c3eb74` on jmhp gave recall@1 0.813, recall@5 0.953, MRR 0.882. 63 of 64 ranks were identical to Ollama's, and one question moved from third to fourth. The standby can replace Ollama without a change in search quality. Afterwards the model was unloaded and the server stopped, and no evaluation containers or volumes were left on jmhp. Recorded in `embedding-approval.md`. The query-instruction comparison belongs to C1's exit. |
| 2026-09-25 | C1 | **In progress: built, tested and measured; the devbox waits on the owner.** `Embedding:QueryInstruction` (`DEVBUDDY_EMBEDDING_QUERY_INSTRUCTION`) is empty by default, one line, and at most 500 characters. `EmbeddingPurpose` is back on the port, vendor-neutral. `IEmbeddingProvider.TextFor(text, purpose)` says what is sent, and by default that is the text unchanged. The HTTP adapter frames a query as `Instruct: <task>` then `Query: <text>` when the setting is set. The gateway takes a required purpose, builds the text to send first, and only then scans it with SB-17. Search embeds as `Query`; the sweep embeds every chunk as `Document`. It is in Compose for `api`, `mcp` and the sweep, and `deployment.md` describes it. **Tests:** 9 new, covering the gateway (framing, the scan seeing the instruction, a secret refused with an instruction set), the adapter (the exact wire form, empty or blank means unchanged, one line only), and search and the sweep passing the right purpose. The egress test `a_clean_search_query_is_sent_once` still proves that an empty setting sends the query unchanged. **Verified on jmhp** at `9af7af1` with `tools/release/setup-and-suite.sh`: .NET **968** passed, format clean, web 78, publish. **Mutation-checked:** four mutations (the scan reading the original text, the sweep embedding as a query, search embedding as a document, the adapter framing documents), each caught by its test. **Measured** with `tools/retrieval/evaluate.sh` against the devbox's model files. With no instruction, the C2 ranks came back exactly. Qwen3's published instruction gave recall@1 0.875, recall@5 0.984, MRR 0.915. A product-specific one gave 0.906, 0.984 and 0.936, but it was written after seeing the set. Recorded in `embedding-approval.md`. **Not done:** setting it on the devbox. That is the owner's choice of instruction and an `info.md` entry, and the devbox's `v1.6.0` does not carry the setting. |
| 2026-09-25 | Defect (supply chain) | **`quay.io/minio/minio` refuses anonymous pulls, so no clean machine can build the evidence store.** Every CI run since `00f552d` failed: `00f552d`, `9c3eb74`, `6c36ff7` and `7ad3406`, 23 quay.io `401 Unauthorized` errors in each. The evidence image build fails in every e2e job, and the MinIO-backed Testcontainers tests fail in the Linux build. The last green run was `7390c6d`, and the C3 diagnostic runs up to about 12:45 UTC on 2026-09-24 passed, so it began between then and 13:04 UTC. Checked from JMPC: quay.io's repository API answers `401 Requires authentication`, and the pinned manifest answers 401 even with an anonymous pull token. jmhp and the devbox still hold the pinned image (`sha256:a1ea29fa…`, amd64), which is why jmhp's runs passed. **Not a C1 fault:** the C1 tests passed in that same run. **These pushes went unnoticed,** because CI was not checked after them. **It blocks** CI, every new installation, and the `v1.7.0` checklist. **Not decided:** how MinIO is obtained from now on. That goes in `info.md`, as the 2026-09-13 move to quay.io did. |
| 2026-09-25 | Defect (supply chain), fixed | **MinIO is built from source**, at the owner's decision (`info.md`). `docker/evidence/Dockerfile` fetches MinIO and `mc` by commit, builds them with Go 1.24.2 pinned by digest, and puts them in a `scratch` image as uid 1000. Both binaries report the upstream release names and commits. `EvidenceStoreTests` builds the same Dockerfile through Testcontainers. The new `DeploymentTests` guard replaces the quay.io one and was mutation-checked four ways. Verified on jmhp at `8f6edad`: .NET 968, format, web 78, and e2e 205 with the restore stage. It also ran confined as Compose runs it, and `mc ready local` answered. **CI passed on clean runners** at `1160df8` (run 36129000799, every job, arm64 included; supply chain 36129000686), so CI is green again for the first time since `7390c6d`. C1's code, pushed in `7ad3406`, passed in that same run. |
| 2026-09-25 | 14.7, A1 | **`v1.7.0` prepared, and its pre-tag checklist passed on jmhp.** The owner asked for the release to be prepared (`info.md`). The proposal is under *14.7* and waits on the owner. `f76c6c0` bumps the Claude plugin to 1.7.0. It adds the `query_instruction` and `evidence_built_from_source` checks, and drops `leftover_dialect`. It was run from `tools/release/` on jmhp, from a bundle, because the commit is not pushed: `setup-and-suite.sh` passed 7 of 7 (.NET **968**, format, web 78, publish), `stack-drill-tokens.sh` 101 of 101, and `upgrade.sh` from published `v1.6.0` on amd64 45 of 45. The evidence volume the `quay.io` image wrote was read byte for byte by the store built from source. `release-matrix.md` has every row. **Not run:** the arm64 upgrade, and everything after the tag. A1's exit still needs the run from the tag on every machine. |
| 2026-09-25 | 14.7 | **Approved as proposed, and the arm64 upgrade passed with a substitute.** The owner approved *14.7*. CI and supply chain passed on `5f435b0`. The Ubuntu arm64 guest and the Mac mini hold no arm64 `quay.io/minio/minio`, so `v1.6.0`'s evidence store cannot be built there. At the owner's choice, `upgrade.sh` gained `DEVBUDDY_PREVIOUS_EVIDENCE_FROM_SOURCE=1` (`f8c6d73`), which builds it from the new commit's source instead. In the guest, `upgrade.sh` passed 45 of 45 from GitHub at `f8c6d73`. The quay-written volume crossing is proven on amd64 only. |
| 2026-09-25 | 14.7, A1, A3, C1 | **`v1.7.0` published, A1, A3 and C1 done.** Tag `v1.7.0` at `b126c27`, after CI 36139496248 and supply chain 36139496021 passed there. Release run 36140305195 passed. **After the tag, every row passed**, each from `tools/release/` at the tag:<ul><li>all seven archives match `SHA256SUMS`; attestations were verified for seven archives and three images with their SBOMs, and a wrong-owner control was refused;</li><li>`post-images.sh` passed 29 of 29 on amd64 (jmhp) and on arm64 (the Ubuntu guest, second run: the first filled the guest's disk compiling MinIO);</li><li>`smoke.sh` passed for `linux-x64`, `linux-musl-x64`, `linux-arm64` natively, `linux-musl-arm64` in Alpine, and `osx-arm64` natively on the M4;</li><li>`client-smoke.ps1` passed for `win-arm64` in the Windows on ARM guest and for `win-x64` natively.</li></ul>It was **published** at 13:39 UTC as Latest, at the owner's approval of *14.7*.<br>**The devbox:** backup `backup-20260925-134018-7c296e236f0d4ba5b` and `.env` were copied to `/data/devbuddy-cache/backups/before-v170-20260925/`. It was checked out at `v1.7.0`, and `DEVBUDDY_EMBEDDING_QUERY_INSTRUCTION` was set to Qwen3's published instruction (C1). It was rebuilt, with MinIO from source, and recreated with the workers profile. Nine migrations, none applied. `/health` answered 200 and an MCP `POST` 401. `scope-report` was clean. `embedding-check` was all ok with the worker's settings. Both workers completed a pass. The instruction is in the environment of `api`, `mcp` and the sweep. Every service runs as its non-root user. **A3:** `stale-sessions.sh` listed **2 of 2** open sessions on the old image `97b96486dfdc`. They were left running. **Plugin 1.7.0** was installed from the tag. A fresh MCP stdio on the new image listed twenty tools, answered `list_projects`, and wrote only JSON-RPC to standard output. **Not checked:** a semantic search through the instruction on the devbox. It was measured on jmhp only. |
| 2026-09-25 | C4 | **In progress: built, and run twice in CI on `ci/zap-scan`.** Runs 36160217189 and 36161438774 passed every job. Both scans finished with exit 2, warnings only, and no finding was High. **Two server errors were found.** The API scan sent `POST /auth/recovery/begin` an address holding a NUL character and got 500: PostgreSQL refuses `0x00` in text, and nothing refuses it first. The answer is the same whether or not the address exists. The servers' logs, now kept for the scans' window, also show `create_work_item` answering 500 for a duplicate key (`ix_work_items_workspace_id_project_id_key`), once per browser, in the e2e test named for refusing it "with the server's reason". That test checks only that an alert appears. **Header findings,** on the client and the API: no CSP, no anti-clickjacking header, no `X-Content-Type-Options`, no Permissions-Policy, and no COOP, COEP or CORP. **Expected:** 41 unmatched GETs answered with the client, and 401s and 404s for a caller with no token. The first run lost the API's logs, because the restore stage replaces the containers; the second kept them in `zap/stack.log`. **Not done:** triage with the owner, and the rules file. |
| 2026-09-26 | C4 | **Fixed and merged: both 500s, the headers, and the lost logs.** At the owner's word (`info.md`). `TextInput` refuses a NUL in any text with 400, in the dispatcher, the `/auth` group and the evidence form. A duplicate work item key answers 409 naming the key, and the row is detached, so the attempt is audited. `SecurityHeaders` sends a CSP fitted to the built client, `frame-ancestors 'none'`, `X-Frame-Options`, `nosniff`, a referrer policy, a permissions policy and CORP on every answer. The e2e run keeps `stack.log` from before its restore stage. **The first CI run of the fixes failed one test**, in Chromium on both architectures: over plain HTTP Chromium ignores COOP and logs an error on every page, which the screen walk collected. COOP and COEP are now sent only when the page arrived over HTTPS, directly or as `X-Forwarded-Proto` says. **Verified in CI** (run 36213830463, every job, arm64 included): the new .NET tests (`TextInputTests`, `ScanFindingTests`, the header check in `AdminUiTests`), the strengthened duplicate-key e2e test, and a new e2e test that fails on any CSP violation the browser reports on every screen, which passed in Chromium, Firefox and WebKit. **ZAP afterwards:** no Medium finding, no 500, and no unhandled exception in the servers' logs for the whole run. What remains is COOP and COEP missing, which is right on a plain-HTTP stack, 41 unmatched GETs answered with the client, and informational findings. **Not done:** the rules file, which would record those as accepted and let a new finding fail a run. |
| 2026-09-26 | C4 | **DONE: the rules file, and the scans fail the run.** At the owner's request. `tests/e2e/zap/rules.tsv` accepts the findings left after the fixes by name, each with its reason, and lists the fixed headers as `FAIL`. `run.sh` fails the run for a `FAIL` finding, for one from a rule the file does not list, for a scan that does not finish, and for any unhandled exception the API logs while scanned, which is how a 500 is caught, because rule 100000 is one rule for a 401 and a 500. **Verified in CI** on `ci/zap-rules` (run 36217441680, every job): both scans passed, no server error, suite 232 of 232, stdio and restore checks clean. **Control** (run 36217472780, a throwaway branch, deleted afterwards): 10109 marked `FAIL` and the NUL filter taken off account recovery. The baseline failed on 10109, the log check failed on 2 unhandled exceptions, and the suite still passed 232 of 232. The Linux build failed on exactly the two `ScanFindingTests` for NUL on recovery, which mutation-checks those tests as well. **Found by the control:** the API scan judges only Low and above, so an informational finding from it never fails a run. The baseline judges every level. Documented in `rules.tsv` and the README. Two faults were fixed before the first run: toggling `set -e` inside `run_zap` would have ended the script at the first failed scan, and the summary step would have failed on a clean run under `pipefail`. |
| 2026-09-26 | Defect (email), fixed | **A mail server that failed turned account recovery into an account-existence oracle.** Found while preparing SMTP for the LXC devbox, by reading `SmtpEmailSender`, which logged nothing and let every failure escape. Recovery awaited it only for an address that has an account, so a broken server answered 500 for such an address and 202 for any other. `create_user_account` also answered 500 after writing the account and its membership, losing the setup token its response returns. The sender now logs the recipient, the subject and the server, never the body, at error level, and returns; a caller that cancelled still sees its cancellation. **Tests:** `SmtpEmailSenderTests` (not thrown, logged without the token, cancellation kept) against a refused port, and `recovery_answers_the_same_way_when_the_mail_server_cannot_be_reached` in `DevBuddy.Api.Tests`. **Verified in CI** on `fix/email-delivery-failure` (run 36238010415, every job). **Control** (run 36238036665, a throwaway branch that threw again after logging): exactly those two tests failed, and nothing else. **Known and not fixed:** delivery still happens inside the request, so with a working server recovery takes longer for an address that has an account. |
| 2026-09-26 | Ports | **The host ports start at 5010**, at the owner's request (`info.md`). Compose publishes the API on `127.0.0.1:5010`, the MCP server on 5011 and Grafana on 5012, each overridable by a variable that sets only the port. The API from source runs on 5013 (https 5014), and Vite on 5015, proxying to 5013; the proxy used to point at 5288 while the API ran on 5140. **Found on the way:** SB-30's `every_published_port_is_bound_to_loopback` recognised only a literal `digits:digits` mapping, so a port given as a variable would have passed off loopback. Its pattern now counts one, and a new test pins the defaults and that. `deployment.md` says what an upgraded installation has to move. The containers still listen on 8080 inside. **Verified in CI** (run 36240391166 on `6a6be20`, every job): .NET 994, the new test among them. |
| 2026-09-26 | Ports, devbox | **The LXC devbox is on the new ports**, at the owner's request, still on `v1.7.0`. Its untracked `compose.override.yaml` publishes the API on `192.168.1.160:5010` rather than 8080, and now sets MCP to `127.0.0.1:5011`, because the tag's own file still says 8081. The old file is kept as `~/compose.override.yaml.bak-20260926-ports`. Only `api` and `mcp` were recreated. **Checked from JMPC over the LAN:** `/health` 200, the UI 200, `/operations` 401, nothing on 8080, and 5011 unreachable. On the devbox, an MCP `POST` to `127.0.0.1:5011` answered 401, and nothing listened on 8081. A fresh stdio session over the plugin's own `devbuddy-mcp` path listed twenty tools, and every line on its standard output was JSON. |
| 2026-09-26 | Backups, devbox | **The LXC devbox backs itself up every night, onto the Proxmox host**, at the owner's request. Until now it had one backup, taken by hand before embeddings, in a volume inside the LXC. The owner created `/srv/devbuddy-backups` on the host (uid 101654, mode 700) and bound it into the LXC as `mp0` at `/mnt/host-backups`; it attached without a restart. The untracked `compose.override.yaml` binds that folder to `/srv/backups` in `api`, `migrate` and `retention`, every service that mounts it, so the retention sweep still prunes backups at 90 days (SB-27). The earlier backup was copied across, and the old `devbuddy_backups` volume is kept. `/data/devbuddy-tools/devbuddy-backup` runs the console's `backup` as the workspace administrator and logs to `~/logs/devbuddy-backup.log`. `jm`'s crontab runs it at 19:30 UTC, 02:30 in Bangkok. **Checked:** a backup by hand, and one from the script with the bare environment cron gives it, both exit 0 and both listed in the host folder. The retention service's next pass read the same folder and deleted nothing. **Not covered:** the host folder is on `pve-root`, the disk the LXC is on, so a failed disk takes both. |
| 2026-09-26 | 14.9 | **`v1.8.0` published, and the devbox on it.** At the owner's instruction. The checklist ran from `tools/release/` at `2317ac4`, the tagged commit, on devrelease, LXC 101 on the Proxmox host, which the owner created for it (`info.md`). **Before the tag:** `setup-and-suite.sh` 7 of 7 (.NET **994**, format, web 78, publish), `stack-drill-tokens.sh` **111 of 111** with the four new checks, and `upgrade.sh` from published `v1.7.0` 45 of 45 on amd64 and on arm64. The first arm64 run failed 4 of 6 before its upgrade began, because the guest's DNS failed while pulling `postgres:17-alpine`; the second passed. CI at the tag passed after re-running one job: Firefox on x64 lost the click on "Set password" in `auth.spec.ts:85`, the fault C3 recorded, for the first time on x64. That click sends no request, so `clickUntilSent` cannot cover it, and a separate task was raised to harden it. **After the tag** (release run 36247214986): all seven archives match `SHA256SUMS`; attestations were verified for seven archives and three images with their SBOMs, and a wrong-owner control was refused; `post-images.sh` passed 29 of 29 on devrelease and in the arm64 guest; `smoke.sh` passed for `linux-x64` and `linux-musl-x64` on devrelease, `linux-arm64` natively and `linux-musl-arm64` in Alpine in the guest, and `osx-arm64` natively on the M4; `client-smoke.ps1` passed for `win-arm64` in its guest and `win-x64` on JMPC. **Published** at 14:20 UTC as Latest. **The devbox:** backup `backup-20260926-142025-a387f84a21af4d62a`, and `.env` and the override copied to `~/backups/before-v180-20260926/`. It was checked out at `v1.8.0`, built, and recreated with the workers profile. The database reported itself up to date, with nine migrations. From the LAN, `/health` and the UI answered 200 and `/operations` 401 on 5010, with the CSP, `X-Frame-Options` and `nosniff`; an MCP `POST` to `127.0.0.1:5011` answered 401. `scope-report` was clean. The application services ran as uid 1654. Both workers completed a pass, and `embedding-check` was ok with the worker's settings. **A3:** `stale-sessions.sh` listed **5 of 5** open sessions on the old image. They were left running. **Plugin 1.8.0** was installed from the tag. A fresh MCP stdio session on the new image listed twenty tools, answered `list_projects`, and wrote only JSON-RPC to standard output. The throwaway stacks were removed. |
| 2026-09-26 | C5 | **In progress: built, and run locally; CI not yet run.** Approved by the owner (`info.md`). Gitleaks 8.30.1 over all 232 commits found three synthetic fixtures (the AWS documentation key in `WorkerAuthorizationTests`, jwt.io's token twice in `SecretCorpusTests`), accepted by fingerprint; nothing else. Trivy 0.74.0 over the repository found **`react-router-dom` 7.9.3 with seven HIGH advisories**, and `bun audit` then found **Vite 7.1.9 with three HIGH** (dev server, Windows paths). `bun audit` had been a warning in CI, so neither was acted on. Both moved within 7.x (7.18.4, 7.3.6); `bun run build` and the 78 web tests passed on the Windows development machine, and `bun audit` is now a gate at HIGH. Trivy's config scan found `cd` inside a `RUN` in `docker/evidence/Dockerfile` (DS-0013, MEDIUM), replaced with `WORKDIR`, and no `HEALTHCHECK` in five Dockerfiles (DS-0026, LOW), accepted until 2027-03-26 because Compose declares each. After that the repository gate passes locally. **Not run anywhere yet:** the image scan (no Docker engine on this machine), CodeQL, and the workflows themselves. `SupplyChainPolicyTests` passed, 4 of 4, and each of its six mutations failed it. |
| 2026-09-26 | B1 | **In progress: the gateway runs, and plain HTTP is off the LAN.** The owner chose an internal CA and Caddy in LXC 100 (`info.md`). `/data/devbuddy-tools/gateway` holds a Compose project with Caddy 2.11.4 pinned by digest, uid 1000, read-only, and `tls internal` for 192.168.1.160. Two faults on the way, both now in `deployment.md`. The image's binary has a file capability, so with every capability dropped it would not exec; `NET_BIND_SERVICE` is kept. A client connecting to an IP sends no SNI, so Caddy refused the handshake until `default_sni` was set. Verified from the devbox with the root as the only trust anchor: `/health` 200 over HTTP/2, `/operations` 401, COOP and COEP present (so `X-Forwarded-Proto` arrives), and a client without the root refused. Port 80 redirects with the path. The API moved to `127.0.0.1:5010` (override backed up to `~/compose.override.yaml.bak-20260926-https`) and came back healthy. From the Windows machine, `http://192.168.1.160:5010` no longer connects, and `openssl s_client` verifies the chain. The plugin's `list_projects` still answers. **Found:** the API reads no `X-Forwarded-For`, so behind any proxy the sign-in limit is shared, and `deployment.md` said the opposite; corrected there. **Left:** the owner trusts the root on each device, then HSTS. |
| 2026-09-26 | B1 | **DONE.** The owner trusted the root on the Windows machine, the Mac mini and the Ubuntu arm64 guest. On the Ubuntu guest `openssl s_client` against the system store and Python's `urllib` both verified. The guest's clock had been 7 h 18 min slow, so the root was "not yet valid". Its time service was switched off: Ubuntu 26.04 uses chrony, not systemd-timesyncd. The owner enabled chrony and it synchronised. Caddy then sends `Strict-Transport-Security: max-age=31536000` (Caddyfile backed up to `~/Caddyfile.bak-20260926-hsts`). Verified: the header is on HTTPS answers, `/health` is 200, and port 80 still redirects. **Its effect is nil while the devbox is reached by IP** (RFC 6797, 8.1.1). |
| 2026-09-26 | C5 | **CI ran on PR #2: seventeen checks passed, one failed as the gate intended.** Supply chain run 36251388666 passed Gitleaks, the repository scan and CodeQL for all three languages. The API, MCP and console images each have eight MEDIUM findings and none above. **The evidence store fails:** `minio` has 44 HIGH and 2 CRITICAL with a fix available, and `mc` 58 and 5. The sources are Go 1.24.2's standard library (for example CVE-2025-68121, CRITICAL, fixed in 1.24.13), gRPC, `golang.org/x/*`, `amqp091-go`, `thrift`, and MinIO itself (CVE-2025-62506, fixed in `RELEASE.2025-10-15T17-29-55Z`). **`minio/minio` has been archived on GitHub since 2026-04-24**, so no later fix will come from upstream. Waits on the owner: see the reply of the same day. Not merged. |
| 2026-09-26 | B1 | **The gateway moved to port 5010**, at the owner's request, who keeps 5030 for another web project (HomeHub). Caddy publishes `192.168.1.160:5010`, and the API stays on `127.0.0.1:5010`. The addresses differ, so the two do not collide. Caddy's `http_redirect` listener wrapper answers plain HTTP on the same port with a 308 to HTTPS, keeping the path and query. 443 and 80 are no longer published. Backups: `~/Caddyfile.bak-20260926-port5010`, `~/gateway-compose.yaml.bak-20260926-port5010`. Verified from the devbox: `https://192.168.1.160:5010/health` 200 over HTTP/2 with HSTS, `http://192.168.1.160:5010/records?x=1` 308 to the same URL over HTTPS, 443 refused, loopback API 200. The owner means to point DDNS at 5010. See the reply of the same day before that happens. |
| 2026-09-26 | C5 | **The evidence store moved to MinIO `RELEASE.2025-10-15T17-29-55Z` and mc `RELEASE.2025-08-13T08-35-41Z`**, the last releases of both, built with Go 1.26.8 pinned by digest. This is the owner's option 1. Built on devrelease from the same Dockerfile: both binaries report their release, their commit and `go1.26.8`. Trivy at the gate's settings found 39 in `minio` and 23 in `mc`, down from 46 and 63. None was in the standard library or MinIO itself. All are in pinned modules: gRPC, `golang.org/x/*`, `amqp091-go`, `thrift`, `prometheus`, `go-jose`, `otel/sdk`, `jsonparser`. These 39 IDs are accepted in `.trivyignore.yaml` until 2026-12-26, each with its path and a statement, and the gate then passed there (exit 0). `stack-drill-tokens.sh` now expects the new release names. **Not yet run:** `EvidenceStoreTests` against the new build (CI runs it). The devbox still runs the old MinIO until its next upgrade. C6 is planned. |
| 2026-09-26 | C6 | **SeaweedFS spike: it fits, with two conditions.** At the owner's word. SeaweedFS is Apache-2.0, not archived, and releases roughly weekly (4.47 on 2026-09-14). **Built** from source at 4.47 (`c5073360`) with Go 1.26.8 pinned by digest, into `scratch` as uid 1000, on devrelease. The binary is 203 MB. **Run** as `weed server -s3`: read-only, every capability dropped, `no-new-privileges`. It needs a tmpfs on `/tmp` owned by uid 1000 (a root-owned one stops it with `permission denied`), and `-logtostderr=true`. **Driven** by a probe using the adapter's exact `AmazonS3Config` and calls on `AWSSDK.S3` 4.0.102.4, SDK checksum defaults included. It passed all eight checks: `PutBucket`/`ListBuckets`; `PutObject` with `AES256` (small, and 860 KB) answered `AES256`; HEAD reported `AES256`; `GetObject` returned the bytes exactly; `DeleteObject` then 404; anonymous 403; wrong secret 403. **At rest:** the plaintext marker appears in no file of the volume. After a restart with the same `WEED_S3_SSE_KEY` the object reads back. With a different key, the read fails with 500 and nothing is disclosed. **Trivy** at the gate's settings: 1 HIGH (`grpc v1.85.0-dev`), against 39 accepted for MinIO. **Memory** is 93 MiB idle, against MinIO's 205 MiB on the devbox. **Condition 1, blocking:** with no `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`, SeaweedFS serves every object, decrypted, to anyone (anonymous GET 200). MinIO refuses to start instead. Compose must require both (`:?`), `DeploymentTests` must check that, and a start-up probe should prove anonymous is refused. **Condition 2:** a scratch image has nothing to run a health check with; a small static Go probe of `/healthz` would stand where `mc ready local` stands. **Not checked:** arm64, the restore of a MinIO backup into it, `EvidenceStoreTests` (bound to the MinIO Testcontainers module), and without a passphrase the KEK is derived from `WEED_S3_SSE_KEY` rather than stored (as logged). |
| 2026-09-27 | B1 | **DevBuddy was reachable from the internet, and now drops it.** The owner had forwarded TCP 8840 on `jennarin.thddns.net` to the gateway: its certificate ("DevBuddy devbox CA") answered there, and 8841 answered with HomeHub's. Access logging was off, so whether anyone else reached it cannot be known. The owner chose WireGuard on UDP 8840 (`info.md`). The Caddyfile now aborts any request not from `private_ranges` and logs access to stderr, with credentials redacted (backup `~/Caddyfile.bak-20260927-lanonly`). Verified: from the LAN it is 200. Through the router's hairpin, which arrives from the public address, HTTPS is cut off. `tools/devbox/vpn/devbuddy-vpn.sh` was tested on devrelease with `wireguard-go` in a server and a client container: install, two peers, a handshake, DevBuddy 200 through the tunnel with Caddy seeing a LAN address, SSH on 192.168.1.160 and .161, HomeHub's 5030 and 1.1.1.1 all timing out even with a peer routing everything, and a removed peer cut off at once. Its kernel check refused correctly where the host had no module. **Left, all the owner's:** load the module on the Proxmox host, create LXC 102, run the script, move the router from TCP to UDP 8840, import each device's configuration. |
| 2026-09-27 | B1 | **The VPN works, and TCP 8840 is closed.** The owner created LXC 102 (`192.168.1.162`, Debian 13, unprivileged), copied the script in through `pct pull 100` and `pct push 102` (sha256 `3822cbce…555c` checked), and ran `install`: `wg0` is up on 10.88.40.1/24 and listening on 8840, and nftables is active. The owner added a peer, `android`, and moved the router's forward to UDP 8840 → `192.168.1.162:8840`. The first attempt still pointed at `192.168.1.160:5010`, which cannot answer WireGuard. The owner reports a handshake and the web UI loading over mobile data. Verified here: Caddy logged `/` and both assets at 200 from `192.168.1.162`, the peer masqueraded as the LXC as designed, and `jennarin.thddns.net:8840` answers nothing over TCP, neither HTTP nor TLS. TCP 8841 to HomeHub is unchanged, as the owner intends. |
| 2026-09-27 | C6 | **ADR-0014 drafted, status Proposed.** At the owner's request, after PR #2 merged (`ffface7`, eighteen checks passing on `7b276e0`). It keeps ADR-0004's decisions except the store, and it rests on facts read from the code. The adapter calls five S3 operations with SSE-S3, and backup and restore go through `IEvidenceBlobStore`, so migrating is a restore. It adds three guards against SeaweedFS's open-by-default behaviour: required credentials in Compose, a health probe that fails unless an unsigned request is refused, and a check in the adapter on first use. It moves SSE to an operator-supplied `WEED_S3_SSE_KEK`. **Two things must be verified before it is Accepted:** the KEK form (the spike used the derived-key form) and arm64. |
| 2026-09-27 | C6 | **ADR-0014's two prerequisites pass: the operator KEK, and arm64.** At the owner's word. The spike's Dockerfile now also builds `healthprobe` (`tools/spikes/c6-seaweedfs/healthprobe/`), a static Go program: `/healthz` must answer 200 and an unsigned `ListBuckets` must be refused with 403. **amd64, devrelease:** with `WEED_S3_SSE_KEK` the server logged it loaded the configured KEK; the adapter probe passed 8 of 8; no plaintext was on the volume; the KEK was not written to the filer (404); a restart read back. A different KEK let the server start and made reads fail with 500. **That corrects the ADR, which had said a mismatch refuses to start, and adds the adapter canary.** With no KEK, an `AES256` write was refused and nothing was written. The health probe said `ok` with credentials and failed without them (the unsigned request got 200). Trivy: 1 HIGH, `grpc` CVE-2026-84445 in `weed`, and none in the probe. **arm64:** the Ubuntu guest's disk filled during the build (5 GB needed, 3.9 GB free). The build's own container, image and cache were removed, and the guest is back to 5.2 GB free, with the pruned Docker build cache included in what was reclaimed. Built natively on the Mac mini instead (Docker Desktop started for it and quit after), `linux arm64`, 192 MB. The same checks passed: 8 of 8, no plaintext in 81 files, restart, a wrong KEK giving 500, and the health probe both ways. A `golang:1.26.8-bookworm` image is left on the Mac mini, because it could not be removed once Docker had quit. ADR-0014 stays Proposed until the owner confirms it. |
| 2026-09-27 | C6 | **ADR-0014 Accepted** by the owner (`info.md`), who asked for it to be merged and built. ADR-0004 is marked superseded, for the choice of store only. PR #14 passed its eighteen checks before the change of status. |
| 2026-09-27 | C6 | **Built on `c6/seaweedfs-evidence-store` (draft PR #15); the release and the devbox move are still to come.** SeaweedFS 4.47 from source into `scratch`, with `healthprobe`. Compose requires the credentials and `WEED_S3_SSE_KEK`, and uses a new volume, `evidence_seaweedfs`. `ObjectStoreSafety` refuses a store that answers an unsigned request, and keeps an SSE canary. **The upgrade path changed from the ADR's wording, at the owner's choice** (`info.md`): a whole restore refuses a database with data, so `restore --evidence-only` writes a backup's bytes beside the rows, checks each against its content hash, and changes no row. `upgrade.sh` backs up on the previous release and runs it after. **CI's first run failed six tests** with 500s. Reproduced on devrelease with the store's logs captured: SeaweedFS pre-grows seven volumes per collection, every bucket is a collection, and `weed server` allows eight, so the second bucket's first write had nowhere to go. The image now sets growth to one volume, `-volume.max=0`, and 1 GB volumes. On devrelease, in the .NET SDK container against Docker: EvidenceStoreTests, EvidenceStoreSafetyTests and RestoreDrillTests passed, 19 of 19. Three mutations each failed their test: no unsigned-request check, no canary, no hash check. The Compose guards' five mutations each failed DeploymentTests. MinIO's 39 accepted findings are removed, and one `grpc` entry for `weed` is accepted until 2026-12-26. |
| 2026-09-27 | C6 | **CI passed on PR #15 at `6cb2431`, all eighteen checks.** The .NET suite ran 1005 tests on Linux with none failed; Infrastructure's 385 include the three safety tests and three evidence-only restore tests. The Playwright runs on amd64 and arm64 passed, in the embeddings, GitHub and observability modes too, as did ZAP, all against the SeaweedFS stack. The evidence image runs as 1000:1000, and Trivy's gate passed: one UNKNOWN, and the accepted `grpc` finding. Left for C6: merging, a release, and moving the devbox. |
| 2026-09-27 | 14.10 | **`v1.9.0` prepared.** At the owner's request, after PR #15 merged at `703881d` with eighteen checks passing on `0c88c85`. Plugin 1.9.0, *14.10* written, and the drill's new check `seaweedfs_guards`. Waiting on the owner: an rc tag or not, and the arm64 machine. The pre-tag checklist has not run. |
| 2026-09-27 | 14.10 | **The pre-tag checklist passed, and `v1.9.0-rc.1` proved `release.yml`.** The owner chose to cut the rc and to run arm64 in the Ubuntu guest, whose disk the owner grew to 70 GB. Claude gave the `growpart` and `resize2fs` commands. On devrelease: part 1 passed 7 of 7, with 1005 .NET tests, and part 3 from `v1.8.0` passed 47 of 47. Part 2 stopped at the new check, on the drill's local `ref`; `59b7dae` fixes the script, and the rerun passed 115 of 115. The arm64 upgrade passed 47 of 47. Both upgrades restored the evidence with `restore --evidence-only`, 1 of 1, with no row changed. The rc's 13 jobs passed. Its provenance and SBOMs verified from outside, it is multi-arch, and it moved no tag. The rc tag and draft are deleted. `release-matrix.md` has the details. **The tag waits on the owner, at `59b7dae`.** |
| 2026-09-27 | 14.10 | **`v1.9.0` published**, as Latest, at the owner's instruction. Tagged at `59b7dae`; release run 36299428311 passed all 13 jobs. After the tag, all passed: `SHA256SUMS` for all seven archives; provenance for every archive and image, with the SBOMs and a wrong-owner control; `post-images.sh` 29 of 29 on devrelease and on the arm64 guest, with identical names; and the smoke test of every archive: `linux-x64` and `linux-musl-x64` on devrelease, `linux-arm64` and `linux-musl-arm64` in the guest, `osx-arm64` on the Mac mini, `win-arm64` in the Windows on ARM guest, and `win-x64` on JMPC. The first `win-x64` attempt ran from Git Bash and could not unpack, because GNU tar read `C:` as a host; it passed from PowerShell. The devbox move is next. |
| 2026-09-27 | 14.10 | **The devbox is on `v1.9.0`**, at the owner's instruction, by `deployment.md`'s steps. Backup `backup-20260927-063821-fbfae3e6856c4f17b` on `v1.8.0`, with `.env` and the override copied to `~/backups/before-v190-20260927/`. `DEVBUDDY_EVIDENCE_SSE_KEK` was generated into `.env` unseen: 64 characters, mode 600. **The owner has to keep a copy off the host.** Checked out at `v1.9.0`, built, and recreated with the workers profile. The evidence store and the API came up healthy on the new volume `devbuddy_evidence_seaweedfs`. MinIO's `devbuddy_evidence` is kept as the way back. `restore --evidence-only` exited 0: the devbox holds no evidence, so it restored 0 of 0. Checked: the store's health probe ok; an unsigned request to it 403; through the gateway `/health` 200 and `/operations` 401; MCP `POST` on 5011 401; no `EvidenceStoreUnsafe` in the API's log; migrations nine; `scope-report` clean; services as uid 1654 and the store as 1000. Both workers completed a pass. **A3:** `stale-sessions.sh` listed 1 of 1 session on the old image, this conversation's own, which was left running. **Plugin 1.9.0** was copied from the tag into the local marketplace and installed. A fresh MCP stdio session over `devbuddy-mcp` listed twenty tools, answered `list_projects`, and wrote only JSON-RPC. |
| 2026-09-27 | 14.11 | **The web client restyled and bilingual, at the owner's request.** The *Ocean Mist Light* theme and sidebar from `demo/`, Thai and English (434 keys), Roboto and Sarabun bundled, and scrolling tables. Verified on the Windows development machine: web suite 85 of 85 (7 new in `i18n.test.tsx`, which caught both a deleted entry and a renamed placeholder when mutated), `bun run build`, and `dotnet build` of the API. A production build served with the exact headers of `SecurityHeaders.cs` loaded every font and logged no policy violation, in Thai and English, at desktop and phone widths. **Not run here:** the .NET suite and the e2e suite; CI and the checklist run them. |
| 2026-09-27 | 14.11 | **The pre-tag checklist passed at `de841f8`.** On devrelease: part 1 passed 7 of 7, with 1005 .NET tests with none failed, the web suite 85 of 85, format and the `linux-x64` publish; part 2 passed 115 of 115; part 3 from published `v1.9.0` passed 47 of 47. The arm64 upgrade in the Ubuntu guest passed 47 of 47. Both upgrades restored the evidence with `restore --evidence-only`, 1 of 1, with no row changed. PR #19 passed all eighteen checks. `release-matrix.md` has the rows. |
| 2026-09-27 | 14.11 | **`v1.10.0` published**, as Latest, at 10:11 UTC. The owner reviewed and merged PR #19 (and #20); Claude's own merge had been refused by the permission classifier until then. Tagged at `de841f8`, the commit the checklist ran at; release run 36309737212 passed all 13 jobs. After the tag, all passed: `SHA256SUMS` for all seven archives; provenance for every archive and image, with the SBOMs (36, 38 and 56 components) and a wrong-owner control; `post-images.sh` 29 of 29 on devrelease and on the arm64 guest, with identical names; and the smoke test of every archive on its own platform. **CI on `81e2d2a`**, the merge of #19, failed one WebKit test, *repeated failures lock the account*; CI on `f71c928` passed with the same code. It failed the same way on Dependabot's #11, and PR #22 fixes the helper: `clickUntilSent`'s window now starts once the button can be pressed. |
| 2026-09-27 | 14.11 | **The devbox is on `v1.10.0`.** Backup `backup-20260927-101144-8249f1dfed414c729` on `v1.9.0`, with `.env` and the override copied to `~/backups/before-v1100-20260927/`. Checked out at the tag, built, and recreated with the workers profile. **`up -d` left the `mcp` service running on the `v1.9.0` image** (`539b03d985ec`) although `devbuddy-mcp` had been rebuilt (`2625bb98ab9b`); `--force-recreate --no-deps mcp` moved it. Checked: through the gateway `/health` 200, `/operations` 401, the UI 200 with its stylesheet and a bundled Roboto font 200, the CSP, `nosniff` and `DENY` headers; MCP `POST` on 5011 401; migrations nine; `scope-report` clean; no `EvidenceStoreUnsafe`; the services as uid 1654 and the store as 1000. Both workers completed a pass. **A3:** `stale-sessions.sh` listed 3 of 3 sessions on older images, the owner's, left running. **Plugin 1.10.0** was copied from the tag into the local marketplace and installed. A fresh MCP stdio session over `devbuddy-mcp` listed twenty tools, answered `list_projects`, and wrote only JSON-RPC. The owner confirmed a copy of `DEVBUDDY_EVIDENCE_SSE_KEK` is kept off the host. |
| 2026-09-27 | B3, D1, C5, C6 | **B3 done: a hosted model server, prepared and not in use**, at the owner's answer (`info.md`). `tools/hosted-model/`: Ollama on an internal network with no port, behind Caddy with TLS, which passes on only `POST /v1/embeddings` with the bearer key; a `pull` service downloads the model because the server has no way out. **Tested on devrelease only**, with `tls internal` on loopback: `check.sh` passed 8 of 8 (no key and a wrong key 401, Ollama's API, a pull and a `GET` 404 with the key, an embedding 200 with 1024 dimensions, plain HTTP a 308), the key did not appear in Caddy's log, both containers ran as uid 1000, and the model's network could not reach the internet while a control on the edge network could. The stack was removed. D1 stays blocked by the owner's choice. C5 and C6 are marked done: their exits were met by `v1.9.0`. |
| 2026-09-27 | Other | **`demo/` removed and Dependabot handled**, at the owner's word. `demo/`, the owner's Ocean Mist Light mock-up, never committed, went to the Recycle Bin; the comments in `Layout.tsx` and `index.css` that named it now name the mock-up. Dependabot: #18, #10 and #5 merged, then #3 (Go 1.27.1 for the evidence store; `CLAUDE.md` updated). #9 was closed as replaced by #18, and #8 because `@vitejs/plugin-react` 6 needs vite 8. #11, #12, #6, #4 and #7 were rebased to be merged once their checks pass. |
| 2026-09-27 | A2 | **Built, not yet run; approved by the owner the same day** (`info.md`). `release.yml` gains three jobs after the draft is cut. `post-images` runs `tools/release/post-images.sh` from the tag on `ubuntu-latest` and `ubuntu-24.04-arm`. `smoke` checks each of the seven archives against the draft's `SHA256SUMS` and runs `smoke.sh` (the Linux RIDs in `ubuntu:24.04` or `alpine:3`, `osx-arm64` on `macos-15`) or `client-smoke.ps1` (`win-arm64` on `windows-11-arm`, `win-x64` on `windows-latest`), each against its own architecture's list of AI operations. `smoke-report` writes every verdict and `FAIL` line into the draft's notes, replacing its own section on a re-run, and says whether the two architectures list the same operations. A failed row fails the run. The x64 rows run too, in addition to the hand runs, because B4 decided only the four arm64 and macOS rows. Verified: actionlint finds nothing new (one old SC2035 note on the checksum step). **Not verified:** any of it on a runner. The exit needs a draft carrying those results, so the next release, or an rc, is its test. |
| 2026-09-27 | B2 | **Done: the Windows archives stay unsigned for now**, at the owner's confirmation (`info.md`). The release notes already say so, and nothing in `release.yml` changes. SignPath Foundation and Azure Trusted Signing are recorded as the options if signing comes back. |
| 2026-09-27 | Docs | **The README, release-readiness and the Thai handbook brought up to `v1.10.0`**, at the owner's request. The README named `v1.3.0` as current and said no worker had run on real data. The handbook (`docs/manual/build-guide.mjs`, regenerated with `bun docs/manual/build-guide.mjs`) still described the 9 September snapshot: MinIO and its KMS key, tokens written to the log, no retention scheduler, no embeddings or worker, and `v1.0.0`'s platforms. It now covers SeaweedFS and `DEVBUDDY_EVIDENCE_SSE_KEK`, the Thai and English client, the `retention` service and the `workers` profile, `restore --evidence-only`, the store's guards, 34 controls, `v1.10.0`'s platforms, the unsigned Windows archives, A2, and the hosted-model kit. `release-readiness.md` records the B2 acceptance. Checked: the generator reports 25 chapters, 63 JSON operations and 20 MCP tools, and the page opened with no console error. |
| 2026-09-27 | Web | **Thai is set in Leelawadee UI where the device has it**, at the owner's request (`info.md`). It ships with Windows under Microsoft's licence, which does not allow bundling, so `--font-sans` names it as an installed font after Roboto; Sarabun stays bundled as the fallback for macOS, Linux and phones. The content security policy is unchanged, since nothing is fetched for an installed font. Verified on the Windows development machine: `bun run build`, the web suite 85 of 85, and the built client served locally in Thai, where `document.fonts.check` found Leelawadee UI and the only font file downloaded was Roboto's, so Sarabun was not fetched. The handbook is regenerated. **Not run here:** the .NET and e2e suites; CI runs them. It reaches the devbox with the next release. |
| 2026-09-27 | A4 | **Designed with the owner, nothing built.** The owner asked how to use the plugin from Codex on the Mac mini, then why it had to be SSH, and decided: HTTPS through the gateway as the main path with SSH kept as the fallback, a machine token scoped to a workspace, a per-user registry outside every repository managed by a `devbuddy` command, the token in the operating system's credential store, one session per workspace, no token for an unregistered checkout, and a .NET client left unsigned. A MAC-address binding was rejected. Smart App Control on the Windows development machine read Off (`VerifiedAndReputablePolicyState` 0). Recorded in `info.md`. |
| 2026-09-27 | A4 | **The server half is written, at the owner's word** (`info.md`): machine tokens instead of access tokens on the HTTP transport, and a rate limit. `MachineTokenAuthenticationHandler` resolves the bearer on every request; the workspace ceiling travels into the caller context as over stdio; the environment is never read for an HTTP request; the transport is at `/mcp`; the limit is per person at the API's defaults, and a request with no working token is not counted. `SessionTokenCheck` and the JWT package left the MCP host. ADR-0006 is amended. **Tests:** `HttpTransportTests` (8). The whole .NET suite passed at 1013 on the Windows development machine, Smart App Control off, and `dotnet format` is clean. **Mutation-checked:** a shared bucket, limiting unauthenticated requests, dropping the ceiling and dropping `RequireAuthorization` each fail a test; reading the environment for a request with no identity survived alone, because authorization refuses such a request first. The e2e client and `mcp.spec.ts` now mint a machine token and use `/mcp`; they type-check and **have not been run**. Not deployed: the gateway route, the client and the packages are still to come. |
| 2026-09-27 | A4 | **The client, the packages and the gateway route are written.** `devbuddy` (ADR-0015) in `src/clients/DevBuddy.Client`, referencing no project: `register`, `list`, `show`, `update`, `unregister`, `token set`, `token remove`, `doctor` and `mcp-headers`. The helper prints the header only for a registered checkout and only when the URL is on its registered server. The token is checked against the server before it is stored, is 43 base64url characters or refused, and is never an argument. Stores: Credential Manager through advapi32, the macOS keychain through Security.framework, and on Linux a mode-600 file. **A change from `info.md`, for the owner to confirm:** Linux gets the file and not Secret Service, because `NoExecutionTests` (SB-04) failed the first version, which ran `security` and `secret-tool`; product code starts no process. Both packages now connect over HTTP: Claude Code through `headersHelper` with `${CLAUDE_PROJECT_DIR}`, Codex through `http_headers_helper` with the same URL twice. The release and supply-chain workflows publish the client and its SBOM; `smoke.sh` and `client-smoke.ps1` check it refuses an unregistered folder. **Tests:** `DevBuddy.Client.Tests` (47, including a real Credential Manager round trip), an end-to-end test in `HttpTransportTests` that runs the built client against the real server, an architecture test, and the package tests rewritten for HTTP. The whole .NET suite passed at 1060 on the Windows development machine, `dotnet format` is clean, and the client publishes for `osx-arm64`, `linux-x64` and `win-x64`. **Mutation-checked:** dropping the URL check, the longest match, the path separator in the prefix match, the server check before storing, and the token format check each fail a test. **Not verified:** the keychain store has not run on a Mac; Codex's helper has not run at all; the e2e suite has not run. |
| 2026-09-27 | A4 | **The devbox runs the branch's MCP server, and the gateway passes `/mcp` to it**, with the owner's approval. Backup `backup-20260927-150951-b5c36138ca124506b`, with `.env`, the override and the gateway's Caddyfile in `~/backups/before-a4-20260927/`. The auto-mode classifier refused Claude's remote writes, so the owner built `devbuddy-mcp:a4` from `4e04362` in `~/devbuddy-a4` (a worktree; `/data` belongs to root), set it in the override, recreated `mcp` and edited the gateway. Checked from the devbox: `mcp` on `devbuddy-mcp:a4`, `/` and `/health` 200, `POST /mcp` 401 with `WWW-Authenticate: Bearer`, `GET /mcp` 405, the old root path 404, no errors logged. The `compose run` containers of stdio sessions carry only their own names as aliases, so `mcp` is the service alone. |
| 2026-09-28 | 14.12, A2 | **`v1.10.1` published, and A2 done.** Pre-tag at `4082c07`: part 1 7 of 7 (.NET 1005, web 85) and part 3 from `v1.10.0` 47 of 47 on devrelease, run on 2026-09-27; part 2 115 of 115 on devrelease on 2026-09-28, rerun from clean after a second start collided with the first run's stack and failed 2; the arm64 upgrade 47 of 47 in the Ubuntu guest. PR #27 merged and the tag pushed. Release run 36375258645 passed all 24 jobs, and **A2's first run** wrote *Runner smoke results* into the draft: `post-images.sh` 29 of 29 on both architectures and all seven archives passing. By hand: checksums, provenance for every archive and image with a wrong-owner control, `post-images.sh` 29 of 29 on devrelease, and the smoke of `linux-x64`, `linux-musl-x64`, `osx-arm64` (Mac mini) and `win-x64` (JMPC). Published at 04:11 UTC as Latest. **Not done by Claude:** the devbox move, which the owner runs (A4 trial pin on `mcp`, `CLAUDE.md`). |
| 2026-09-28 | 14.12 | **The devbox is on `v1.10.1`**, moved by the owner with the commands Claude gave: the nightly backup script, `.env` and the override copied to `~/backups/before-v1101-20260928/`, checkout of the tag, a build of every service **except `mcp`**, whose A4 trial pin (`devbuddy-mcp:a4`, PR #25) a build would have overwritten, and `up -d --no-build`. The owner saw `/health` 200 through the gateway. Claude then checked, read-only: `git describe` is `v1.10.1`; `api`, `evidence`, `retention` and both workers were recreated at 04:20 UTC and `mcp` still runs `devbuddy-mcp:a4`; services as uid 1654 and the store as 1000; `/operations` 401; the served stylesheet names Leelawadee UI; migrations nine; both workers completed a pass; no `EvidenceStoreUnsafe`. Plugin 1.10.1 is installed on JMPC. |
| 2026-09-28 | A4 | **Codex on the Mac mini reached DevBuddy over HTTPS through the client.** On the owner's Mac, from a macOS build of `4e04362`: `devbuddy register` and `doctor` passed for two checkouts in one workspace, so the keychain store works and the Mac trusts the gateway's CA. Codex 0.157.1 connected (initialize, tools/list, all 200 in the gateway's log), so **Codex runs `http_headers_helper` in the session's folder** and no `--dir` is needed; then `list_projects` answered the workspace's three projects. **Two faults it found, fixed on the branch:** without the package's `AGENTS.md`, Codex answered "list devbuddy projects" from `devbuddy list`, reading registrations as DevBuddy's projects; and in Codex's sandbox the keychain is out of reach, so `devbuddy show --json` threw a stack trace and Codex then called `list_projects` without a workspace. `show --json` no longer reads the store and gives the MCP URL too; `show`, `doctor` and `mcp-headers` say the store could not be read; any other failure is one sentence; `list` says it lists registrations; both instruction files say to ask DevBuddy through its tools and use `devbuddy show --json` for the workspace. `SandboxTests` (5): the client suite passes at 52 on the Windows machine. `HttpTransportTests` could not run there that day, Docker not running; CI runs them. |
| 2026-09-28 | 14.13, A4 | **`v1.11.0` published.** Pre-tag: part 1 7 of 7 (.NET **1065**, web 85) and part 3 from `v1.10.1` 47 of 47 on devrelease at `2b7f854`; part 3 47 of 47 in the Ubuntu arm64 guest. Part 2 failed 1 of 115 on a checklist probe still aimed at the MCP root; PR #31 moved it and `post-images.sh`'s to `/mcp` and added a machine-token check over HTTP, and part 2 passed 116 of 116 on the same build. The owner merged #31 and pushed the tag at `d2fda9b`. `main` CI needed one rerun for a WebKit crash that predates A4 (a task of its own). Release run 36393885247 passed all 24 jobs, runner smoke on every archive with the client rows. By hand: checksums, provenance for every archive and image with wrong-owner and wrong-tag controls, `post-images.sh` 29 of 29 on devrelease, and smoke of `linux-x64`, `linux-musl-x64`, `osx-arm64` (Mac mini) and `win-x64` (JMPC). Published at 10:23 UTC as Latest. **A4 stays IN PROGRESS** until its exit criterion's other half, Claude Code on the Windows machine over HTTPS, and the devbox's move to the tag. |
| 2026-09-28 | 14.13, A4 | **The devbox is on `v1.11.0`**, moved by the owner with the commands Claude gave: the nightly backup script (exit 0 at 12:52 UTC), `.env` and the override copied to `~/backups/before-v1110-20260928/`, the `image: devbuddy-mcp:a4` pin removed from the override, the tag checked out, `compose --profile workers build`, `up -d --no-build` and `mcp` recreated. The owner saw `v1.11.0`, `api` healthy, `/health` 200 and `POST /mcp` 401 through the gateway. Claude then checked, read-only: `mcp` runs `devbuddy-mcp:latest` built at 12:54 UTC, no pin left, migrations nine, no error in the logs. The build rebuilt `api` and `mcp` only: `retention`, both workers, `migrate` and `evidence` still run their `v1.10.1` builds, and nothing they are built from changed between the two tags. **Three stdio sessions opened before the upgrade are still on the trial image** (`mcp-run`, 7 to 10 hours old): restarting them from the client moves them (A3). The gateway already passed `/mcp`. |
| 2026-09-28 | 14.14 | **`v1.11.1` published.** #33 (`/.well-known/` is 404) merged after one rerun for the WebKit crash; #34 prepared the release. Pre-tag at `2075492`: part 1 7 of 7 (.NET **1068**), part 2 116 of 116 and part 3 from `v1.11.0` 47 of 47 on devrelease; part 3 47 of 47 in the Ubuntu arm64 guest. Tagged at `2075492`; release run 36439479931 passed all 23 jobs with runner smoke on every archive. By hand: checksums, provenance for every archive and image with wrong-owner and wrong-tag controls, `post-images.sh` 29 of 29 on devrelease, and smoke of `linux-x64`, `linux-musl-x64`, `osx-arm64` (Mac mini) and `win-x64` (JMPC). Published at 15:23 UTC as Latest. |
| 2026-09-28 | A4 | **`devbuddy register` takes a folder that is not a git repository**, at the owner's choice (`info.md`), so an SA or BA working in documents can register. Outside a repository the folder itself is the checkout. The root of a drive, the home folder and a folder above it are refused before the token is asked for, including a home folder that is itself a repository. `ClientContext` takes the home folder so a test can give it one of its own. ADR-0015 says so. **Tests:** 5 new or rewritten in `RegisterTests` and `StoreAndRegistryTests`; the client suite passes at 57 on the Windows development machine and `dotnet format` is clean. **Mutation-checked:** a breadth check that refuses nothing fails 4 tests. `HttpTransportTests`, which drives the built client against the real server, passes 9 of 9. **Not run:** the whole .NET suite. Whether Cowork can run the header helper is not answered by this. |
| 2026-09-28 | A4 | **`devbuddy mcp-bridge`, for Cowork, written and on trial**, at the owner's request. Cowork does not read `headersHelper` from a plugin's `.mcp.json` but starts a plugin's local stdio server on the host, so the bridge serves MCP over stdio and posts each message to the registered checkout's `/mcp` with its token. It takes no URL; it answers a refusal, a missing token, an untrusted certificate or an unreachable server with a JSON-RPC error saying why; it writes UTF-8 with no byte order mark whatever the console's code page; it waits for `initialize` and sends the negotiated `MCP-Protocol-Version` after it; and it adds the workspace and default project to the server's `instructions`, since the skill's `devbuddy show --json` cannot run in Cowork's VM. **Tests:** `McpBridgeTests` (13); the client suite passes at 70 on the Windows development machine and `dotnet format` is clean. **Mutation-checked:** no instructions, no refusal on an HTTP error, and not waiting for `initialize` each fail a test. **Run against the devbox** from the Windows development machine: `initialize`, 20 tools and `list_projects` through the gateway. **Not verified:** Cowork itself, which the owner installs the trial plugin into; how Cowork tells a local server which folder it works in; macOS and Linux. |
| 2026-09-28 | A4 | **The bridge ran in Cowork, and now supplies the workspace itself.** The owner installed the trial plugin. A Cowork project with threads never reached it (it said the program could not be found, as a session away from this machine would). A local Cowork task did: the log shows the bridge started by `local-agent-mode-sessions`, in `C:\WINDOWS\System32` with only `CLAUDE_PLUGIN_ROOT` set, then `initialize`, `tools/list` and a `tools/call`, all through the gateway. So Cowork starts a plugin's stdio server on the host, and the credential store, the LAN and the gateway's CA all work from there. **The call was refused**: the assistant never saw the workspace in the server's instructions, wrote one of its own, and the server refused the token there. The bridge now takes `workspaceId` out of every tool schema and puts the checkout's into every call, at the top or in `scope` as the tool takes it, replacing whatever the assistant wrote. **Tests:** 3 more in `McpBridgeTests`; the client suite passes at 73; removing the filling fails 2 and removing the hiding fails 1. Against the devbox, `list_projects` with no arguments answered HomeHub, and `list_records` with a made-up workspace answered the real one's records. **Found, not yet handled:** Cowork tells a local server nothing about the folder it works in, so `--dir` cannot come from Cowork. |
| 2026-09-29 | A4 | **The bridge needs no folder, and `plugins/cowork/` exists**, at the owner's request. The bridge takes, in order, a `--server` and `--workspace` it was given, the registered checkout holding the folder, or the one server and workspace every registration shares, and refuses when there are two rather than choose. **The last was Claude's proposal; the owner confirmed it on 2026-09-29** (`info.md`): Cowork gives a local server no folder, and with it a person with one workspace needs no configuration. A setting left unexpanded (`${...}`) counts as absent. The token is keyed by server, so a `--server` naming another finds none to send. `plugins/cowork/` runs `devbuddy mcp-bridge` by name, names no server, and has a skill written for Cowork: the workspace is supplied, the shell cannot reach DevBuddy, and `aiScopeUnstructured` is explained, because Cowork's assistant read it as data being closed to AI. `plugin-hosts.md` has the steps. **Tests:** 8 more in `McpBridgeTests` (client suite 81), and `PluginPackageTests` covers the third package (14): naming a human-only operation in its skill fails three of them. Removing the fallback, and choosing the first of two workspaces, each fail a test. **Run in Cowork** by the owner on 2026-09-29, from the committed package with two changes for the machine: the client's absolute path, because it is not on `PATH` there, and `--log`. The log shows the bridge started in `C:\WINDOWS\System32` with no `--dir`, took the only registered workspace, and answered `initialize`, `tools/list` and a `tools/call`; the owner reports it works. **Not verified:** the package exactly as committed, which starts `devbuddy` by name; macOS and Linux. |
| 2026-09-29 | A4 | **Cowork chooses its workspace, once per task**, at the owner's choice (`info.md`). A probe first logged Cowork's `initialize`: its capabilities carry no MCP `roots`, so a task's folder cannot decide. So with more than one workspace registered the bridge holds every tool call, answering that a workspace has to be chosen, and adds `use_workspace`, which the assistant calls with the person's answer. A second choice of another workspace is refused, so one task reads one workspace; a workspace with no token on the machine is refused without being chosen. With one workspace nothing changes. With nothing usable, nothing registered or a bad `--server`, the bridge now starts anyway and every call answers with the reason, instead of exiting where Cowork showed only a failed connector. `devbuddy register --label` and `update --label` name a workspace for the question; without one, the registered folders name it. The Cowork skill tells the assistant to ask and not choose. **Tests:** the client suite passes at 89; holding calls, locking the choice, offering the tool and refusing a tokenless workspace each fail a test when removed. **Against the devbox**, with a trial registry of the real workspace and a made-up second one: a call before choosing was held, choosing the real one answered `list_projects`, a second choice was refused, and the made-up one had no token. **Not verified:** in Cowork, whether its assistant asks the person rather than choosing. |
| 2026-09-29 | A4 | **The choice ran in Cowork, and a plugin uploaded to Cowork also loads in Claude Desktop's Code tab.** The owner installed the choice trial (the committed package, the client's absolute path, `--log`, and a registry of its own holding the real workspace and a made-up one). Cowork's assistant said it had to ask, asked the owner which workspace, and passed the answer on: the made-up one was refused for having no token, without being chosen, and the assistant asked again; HomeHub was chosen and `list_projects` answered. **Found:** at 09:26:13 a second bridge started with `CLAUDE_PROJECT_DIR=C:\Codes\devbuddy-plugin` and `CLAUDE_CODE_ENTRYPOINT=claude-desktop` but Cowork's `CLAUDE_PLUGIN_ROOT`: the Code tab session in that checkout had loaded the Cowork upload, whose name, `devbuddy`, is also the Claude Code package's, and its assistant received the bridge's two-workspace instructions. The Code tab starts the bridge in the project folder, so with the real registry the folder decides and nothing is asked. It also sent `server/discover`, which the server answers 400 and the bridge passes on as an error. **Open, for the owner:** whether the two packages keep one name. |
| 2026-09-29 | A4 | **The Cowork package is `devbuddy-cowork`**, at the owner's choice (`info.md`), so a Cowork upload no longer takes the Claude package's place in Claude Desktop's Code tab. A Code tab session holding both lists DevBuddy twice; for a registered folder both reach the same workspace. `PluginPackageTests` holds the two names apart (15; putting the old name back fails two). Merging the packages into one bridge-based package was offered and not chosen. |
| 2026-09-29 | A4 | **The owner confirmed that one registered workspace is used without asking**, recorded in `info.md`, and asked for PR #37 to be merged. After the rename, the Code tab session in this checkout listed the two packages apart, `devbuddy` over HTTP and `devbuddy-cowork` through the bridge. |
| 2026-09-29 | A4 | **Prepared for the release that carries the Cowork bridge**, at the owner's request. `plugins/cowork` is 1.12.0, proposing `v1.12.0`, a minor version because it adds a command and a package; the Claude package moves with the release commit as usual. `plugin-hosts.md` says how to put the client on `PATH`, which only Cowork cannot do without, and that Claude Desktop must be restarted to see it. `smoke.sh` and `client-smoke.ps1` start `devbuddy mcp-bridge` from the published archive with nothing registered: it must exit 0 once its input closes, answer both messages, begin with `{`, answer `initialize` itself, and answer a tool call with why. **Run** on archives built the way `release.yml` builds them: `client-smoke.ps1` on `win-x64` on the Windows development machine, 0 failed; `smoke.sh` in `ubuntu:24.04` on devrelease, 0 failed, after re-packing there, because tar on Windows drops the execute bits. |
| 2026-09-29 | 14.15 | **`v1.12.0` prepared**, at the owner's request, on top of PR #38: both plugins at 1.12.0, *14.15* written, and `info.md`. The server is unchanged since `v1.11.1`, so the upgrade is `up -d` alone; people's machines take the new client, and Cowork users add it to `PATH` and upload `plugins/cowork/`. The pre-tag checklist has not run. |
| 2026-09-29 | 14.15 | **The pre-tag checklist for `v1.12.0` passed** at `235a17c`, the merge of PR #39, at the owner's request. On devrelease: part 1 7 of 7, with 1108 .NET tests (89 the client's) and the web suite 85; part 2 116 of 116; part 3 from `v1.11.1` 47 of 47. In the Ubuntu arm64 guest, the upgrade from `v1.11.1` 47 of 47. devrelease had 8.8 GB free, so old release images were removed first (`release-matrix.md`). A first start on devrelease ran nothing, because a path in the command was expanded to nothing on JMPC; it was started again. **The tag waits on the owner.** |
| 2026-09-28 | Defect (web client, e2e) | **WebKit stopped painting the sign-in page when the primary button's hover brightness was animated, and the theme brought that in.** The `End-to-end (Playwright, ubuntu-latest)` job failed intermittently in WebKit only from CI run 36309277392, the merge of the Ocean Mist theme (#19, 2026-09-27), and never before it: 16 of 17 failed jobs from then to run 36380709826 were WebKit, in `auth.spec.ts:48` (lockout) or `administration.spec.ts:8` (once `:43`). The seventeenth was Firefox on arm64 in `projects.spec.ts:21`, a different fault. Playwright has been 1.63.0 since 2026-09-17, so it is not the browser version. **What the traces show:** the page's last painted frame is from during a sign-in request, and after that no frame is painted, so `click` waits forever for the button to be "stable", which takes two animation frames. On the runner that ended as `Target crashed` (runs 36383598188 and 36380709826), as a request that never left, or as an alert not found. WebKit's summed test time on amd64 went from 165 and 234 s in the two runs before the theme to 542 and 550 s after it, and the invite test from 6 to 22 s. **Reproduced on JMPC in Docker** (28 CPUs), so memory and CPU are not the cause: the lockout test hung 5 of 30 times in WebKit, the invite and role tests 0 of 60. **Bisected** in a throwaway spec that rewrote the served stylesheet through `page.route`. The fonts, the fixed body background, the gradient and the shadows all still hung (37 of 200 in all), and turning transitions off hung 0 of 50. Then, on the button alone, 30 runs each: as it is 3, no gradient 6, Tailwind's discrete transitions removed 7; **no `filter` 0, only `opacity` animated 0, everything but `filter` animated 0.** The theme gave the primary button `hover:brightness-110`, and Tailwind's plain `transition` animates `filter`. Playwright's click leaves the pointer over the button while `disabled` toggles its opacity, and animating that filter is what stops WebKit on Linux. **Fixed in the client:** `Button` animates `color, background-color, border-color, opacity, box-shadow` and not `filter`. The brightness still applies on hover, at once rather than faded. `web/admin/test/button.test.tsx` holds every variant to that and fails with the old class list (web **86**). **And in the test:** `clickUntilSent` read an `isEnabled` that threw because the button had just gone (the sign-in succeeded in between, run 36328568141) as a lost press. It now returns only when the button is no longer in the page; a button still there that cannot answer stays a failure. No test was skipped, no timeout raised, and retries stay off. **Verified on JMPC:** the three tests 60 times each in WebKit, 180 of 180; then the whole suite, 214 passed and 21 skipped, stdio 8 of 8, and restore 6 of 6. **Not verified:** CI on the branch, not yet run. Whether Safari on macOS, with a GPU, was ever affected has not been checked. |
| 2026-09-29 | 14.16 | **The WebKit fix brought onto `main` for `v1.12.1`.** The row above was written by the session that found it, which left its change uncommitted on the local branch `e2e/webkit-crash` and ended. Claude applied that change unaltered to `release/v1.12.1` from `main`: `ui.tsx`, `fixtures.ts` and `web/admin/test/button.test.tsx`. On the Windows development machine the web build passed and the web suite passed at **86**; restoring `main`'s `ui.tsx` fails the new test. The Claude and Cowork plugins move to 1.12.1. |
