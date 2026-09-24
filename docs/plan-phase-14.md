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
```

A1 comes first because every later release runs the checklist, and the next one would otherwise be
copied by `sed` again. C2 comes before C1 because a change to retrieval that nothing measures is a
guess.

---

## A — Work that can start at once

### A1 — The release checklist, in the repository
**Status: IN PROGRESS**

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
**Status: IN PROGRESS.** The documentation, the listing and the check are done. The exit still
needs the next devbox upgrade to record its count.

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
**Status: TODO. It waits on C2.**

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
**Status: TODO**

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
**Status: TODO**

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

---

## D — Carried over

### D1 — Phase 13 C8, the hosted mode against a real server
**Status: BLOCKED (no hosted server).** It follows B3's answer.

---

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
