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
**Status: TODO.** B4 is answered, so it can start after A1.

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
**Status: TODO. It waits on the owner.**

- **Problem:** Smart App Control refuses a new unsigned build, which the development machine has
  already hit. Every release's notes say the archives are unsigned.
- **Options:**
  1. **Azure Trusted Signing**, which costs a monthly fee and needs an identity validation.
  2. **An OV code-signing certificate**, bought yearly.
  3. **Stay unsigned**, and keep stating it.
- **If signed:** `release.yml` signs the `.exe` apphosts for `win-x64` and `win-arm64` before the
  archive and its attestation are made. A test in the checklist checks the signature.

### B3 — A hosted model server of the owner's own (Phase 13 C8)
**Status: TODO. It waits on the owner.**

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
**Status: IN PROGRESS.** Approved 2026-09-26 (`info.md`), from a comparison with the HomeHub rule
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
**Status: TODO. Planned at the owner's word (`info.md`, 2026-09-26). Nothing is built until the
owner chooses a store.**

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
**Status: BLOCKED (no hosted server).** It follows B3's answer.

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
