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
**Status: TODO. It waits on the owner.**

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
**Status: IN PROGRESS.** Asked for and approved on 2026-09-25 (`info.md`). It is built, and the
first run's findings wait on triage with the owner.

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
