# Phase 13 — Closing what v1.3.0 left open

**Status: APPROVED 2026-09-21** by the project owner (`info.md`, *Confirmed Phase 13* the same
day). This file is the plan of record for Phase 13 and its progress log. `docs/plan.md` keeps
Phases 0 to 12 and points here.

**Rule for this file:** every time a work item is finished, its status line changes and a dated
entry goes into the [Progress log](#progress-log) at the bottom. The entry says what was verified,
where, and what was not. An item is `DONE` only when its exit criterion is met, and that follows the
standard the rest of the project uses. `IMPLEMENTED` is not `TESTED`, and a build that was not run
is reported as not run.

Status values: `TODO`, `IN PROGRESS`, `BLOCKED (reason)`, `DONE`, `CLOSED — NOT POSSIBLE (reason)`.

## Where this came from

On 2026-09-21 the open items after `v1.3.0` were listed in four groups:

- **A:** work that could start at once.
- **B:** decisions waiting on the owner.
- **C:** gaps in test coverage.
- **D:** accepted limitations.

The owner's instruction was:

- **A:** do all of it.
- **B:** do all of it except HTTPS for the devbox and `win-arm64`.
- **C:** do all of it.
- **D:** do all of it.

Answers the owner gave the same day:

- **B7:** approve other installations through a written checklist. No generative model.
- **B8:** add a narrow role for the stale-record sweep rather than using an Administrator token.
- **B9:** build the bounded-scope approval flow, and approve no scope on the devbox yet.
- **B6:** Voyage AI, chosen after comparing it with OpenAI and Cohere (table under B6).
- **A1:** the version is `v1.4.0`.

## Out of scope, by the owner's instruction

- **HTTPS for the devbox installation.** It stays deferred and LAN-only (`info.md`, 2026-09-17).
- **`win-arm64`.** It stays in the built-but-unverified tier.

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

Two further rules for this phase:

- **Every new operation declares its AI exposure.** Every item here is human-only unless it says
  otherwise. The AI surface stays at twenty.
- **Enabling the hosted embedding mode on an installation needs three things:** the vendor named,
  an allow-list entry, and an acceptance of its own. Building and testing the adapter does not
  enable it anywhere.

## Order of work

```
13.1  A4 documentation            (no code)
13.2  A1–A3 release v1.4.0        (current main; carries the project-scope security fix to devbox)
13.3  B8 IndexMaintainer role     → enable stale-record-sweep on devbox
13.4  D  limitations, in code     (D1–D8)
13.5  B9 bounded-scope flow
13.6  B7 installation checklist
13.7  B6 hosted provider          (Voyage AI)
13.8  C  test coverage            (C1–C8)
13.9  B10 linux-musl-arm64 → verified;  C9 arm64 on server-class hardware
13.10 release v1.5.0, devbox to that tag
```

13.2 comes early because the devbox runs a detached `main` commit (`3e7cd47`, since 2026-09-18),
not a release. It already carries `958272a`, the project-scope fix. What it lacks is a tag
that went through the checklist, and `info.md` (2026-09-17) says the devbox runs from tags.

---

## A — Work that can start at once

### A1 — Release `v1.4.0` from current `main`
**Status: DONE (2026-09-22)** — published at the owner's instruction, Latest.

- **Version:** `v1.4.0`, a minor release, because it adds capability:
  - the screens for every operation;
  - `list_source_repositories`;
  - `scope-report`.

  Confirmed by the owner on 2026-09-21.
- **Contents since `v1.3.0` (`c850275`):** 30 commits.
  - Screens for every operation, and `list_source_repositories`.
  - The project-scope check (`958272a`) and `scope-report` (`d07a452`).
  - Losing the evidence-bucket race (`3e585b4`).
  - Parseable console output, and the two false log alarms gone (`0fad716`).
  - Archived and superseded revisions out of semantic search (`9c999df`).
  - The audit-reference length check (`16e3f25`).
  - The admin screen kept mounted while the session is re-read (`80b167c`).
  - The Playwright suite and its CI job.
- **Steps:**
  1. Count the tests on the release commit. Run the full .NET suite in the SDK container on jmhp
     and `bun test` in `web/admin`.
  2. Run `bash tests/e2e/run.sh`, and confirm CI is green on the commit, all four jobs.
  3. Write the release notes:
     - the upgrade note for `v1.3.0` → `v1.4.0`;
     - that `scope-report` should be run once after upgrading;
     - verbatim, what was and was not run for `win-arm64` and `linux-musl-arm64`.
  4. Push tag `v1.4.0`. `release.yml` builds, signs, and publishes a draft.
  5. Run the checklist in full, with nothing carried over (`docs/operations/release-matrix.md`):
     - `win-x64` smoke test;
     - `linux-arm64` on the Ubuntu arm64 guest;
     - `osx-arm64` on the Mac mini;
     - the restore drill with both volumes destroyed;
     - attestation verification from outside the workflow.
  6. Record the results in `release-matrix.md`, and publish the draft on the owner's instruction.
- **Exit:**
  - the tag is published;
  - the checklist row for `v1.4.0` is complete in `release-matrix.md`;
  - an `info.md` entry records the cut.

### A2 — Move the devbox onto the `v1.4.0` tag
**Status: DONE (2026-09-22)** — the devbox runs the `v1.4.0` tag.

- **Steps:**
  1. `ssh devbox`, then in `/data/devbuddy` check out `v1.4.0`, or pull the published images.
  2. Run `migrate` and restart the stack, with the `workers` profile kept.
  3. Run `scope-report`. On 2026-09-17 it reported none; confirm that still holds.
  4. Probe `/health`.
  5. Probe the MCP server with a `POST`, not a `GET`.
  6. Run one embedding pass, and check `search_similar_records` over the plugin.
- **Exit:** the devbox reports `v1.4.0` and passes the probes. It currently runs `3e7cd47`, and
  the tag adds only documentation on top of that. The memory note and `info.md` say it
  runs a tag.

### A3 — Plugin package version
**Status: DONE (2026-09-22)** — plugin 1.4.0 installed; a tool call succeeded through it.

- **Steps:**
  1. Bump `plugins/claude/.claude-plugin/plugin.json` to `1.4.0`. The Codex package carries no
     version.
  2. Confirm `PluginPackageTests` still passes.
  3. Reinstall the plugin locally and call `list_projects` through it.
- **Exit:** the package version matches the release, and one tool call succeeds through the
  installed plugin.

### A4 — Stale documentation
**Status: DONE (2026-09-21)**

- **Steps:**
  1. `CLAUDE.md`: the SB-19 paragraph says "Not yet run in CI". CI has run the full suite, green, on
     every push since then, including `35328802725` on 2026-09-18. Replace it with what ran and
     where.
  2. `docs/plan.md`, "Remaining open items": the paragraph "What is left is a caller and a
     schedule" predates the schedule. Rewrite it so the section ends at Phase 12 and points to
     this file.
  3. `docs/plan.md` line 151, Phase 0: "has not yet run on GitHub". Annotate it as historical;
     CI has run since.
  4. `AGENTS.md`: "Phase 0 to Phase 12" becomes "to Phase 13", with a pointer here.
- **Exit:** no document claims something about CI or the plan's status that is no longer true.

### A5 — The web client reads one grant per workspace
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- `useWorkspace` in `web/admin/src/api/session.tsx` returns the first `WorkspaceAccess` for a
  workspace, and navigation is built from that grant's permissions. A person holding two grants in
  one workspace, for example a project-scoped Reviewer and a workspace-wide Viewer, sees the menu
  of whichever grant came first. The server is unaffected, because it authorizes every request
  itself.
- **Fix:**
  - build workspace navigation from the union of the person's workspace-wide grants;
  - build project navigation from the workspace-wide grants plus the grants on that project.
- **Tests:** web unit tests for the merge, and an e2e test with two grants.

---

## B — Owner decisions, now taken

### B6 — Hosted embedding provider
**Status: DONE (2026-09-21)** for the adapter, merged to `main`. Enabling it anywhere is not part of this item and needs an acceptance per installation.

**Superseded 2026-09-23:** the owner withdrew Voyage AI, and its dialect is removed from the code. What follows is the record of what B6 decided and built, and not the current state (`info.md`).

`HttpEmbeddingProvider` speaks the OpenAI-compatible `/embeddings` shape: `input` in, and
`data[].embedding` out. How the three candidates differ in what this project has to do:

| | Wire format | Work here | Notes |
|---|---|---|---|
| **OpenAI** (`api.openai.com`) | Native `/v1/embeddings`, exactly the shape the adapter speaks | Configuration and tests only | `text-embedding-3-small` or `-large`. Dimensions can be shortened by a request parameter. |
| **Voyage AI** (`api.voyageai.com`) | `/v1/embeddings`, the same `input`, `model` and `data[].embedding` | A small addition: `input_type` (`document` or `query`), which improves retrieval and which the port does not yet pass | Dedicated embedding models, strong multilingual support including Thai. The vendor Anthropic recommends for embeddings. |
| **Cohere** (`api.cohere.com`) | `/v2/embed`, with `texts` in and `embeddings.float` out, a different shape | A second adapter class, and its own tests | `embed-multilingual-v3.0`. Also takes an `input_type`. |

- **Steps once named:**
  1. If the vendor needs it, add a `Purpose` (document or query) to the port call. The sweep passes
     `document` and `search_similar_records` passes `query`.
  2. Add a vendor-specific adapter, or configuration only.
  3. Add tests against a recording HTTP double:
     - the request shape;
     - the `Authorization` header present and never logged;
     - `EmbeddingOptions.Problems` refusing to start a hosted provider whose host is not in
       `OutboundAccess:AllowedHosts`;
     - SB-17 scanning before anything is sent;
     - a short answer refused.
  4. Extend SB-34's evidence in `verification-matrix.md`.
  5. Write the deployment section in `docs/operations/deployment.md`.
  6. Write a template for the acceptance entry in `info.md`, which the owner has to confirm
     separately before any installation enables it.
- **Not done by Claude:** entering the API key into any configuration. The owner sets
  `DEVBUDDY_Embedding__ApiKey` on the server.
- **Exit:**
  - the adapter and tests pass in CI;
  - the mode is off by default, and a test asserts that;
  - the acceptance template exists.

  Enabling it on a real installation is a separate owner acceptance and is outside this exit.

### B7 — Approving the provider and worker on other installations
**Status: DONE (2026-09-22)** — run on the devbox, output in the log.

- **Steps:**
  1. Write `docs/operations/embedding-approval.md`. It covers:
     - what an installation must show before the self-hosted provider runs against real data
       (pgvector image by digest, model server with no published port, the worker's own Viewer
       account and token, a budget, SB-18 in force);
     - the `info.md` entry template, modelled on the devbox entry of 2026-09-16;
     - the rollback, which is revoking the token and dropping the index rows.
  2. Add a read-only console command, `embedding-check`, that reports:
     - whether pgvector is present;
     - the configured provider mode and model;
     - the model's dimension against the configuration;
     - whether the worker token resolves, and its role;
     - the budget.

     It exits non-zero on a problem. It is outside the pipeline, like `scope-report`, and changes
     nothing.
  3. Add tests for `embedding-check`: each problem is reported, and a healthy configuration exits 0.
- **No generative model.** That would be a new capability and needs an ADR (owner, 2026-09-21).
- **Exit:** the document and command exist, the tests pass, and the command has been run once on
  the devbox with the output recorded.

### B8 — A narrow role for the stale-record sweep
**Status: DONE (2026-09-22)** — shipped in `v1.5.0`; running on the devbox as an `IndexMaintainer` account.

The problem: `stale-record-sweep` needs `ManageIndex`, and only `Administrator` carries it
(`RolePermissions.cs:50`). A token for it would reach everything an administrator reaches.

- **Design:**
  - Add `Role.IndexMaintainer = 5` in `DevBuddy.Domain/Access/Role.cs`. The new number is appended,
    because roles are stored as integers.
  - Its permissions: `ReadKnowledge` and `ManageIndex`, and nothing else.
  - Not grantable over the AI channel, like every grant.
- **The trap, found while reading for this item (2026-09-21):**
  - `AuthorizationService.EffectiveRoleAsync` (`AuthorizationService.cs:140`) picks the
    *numerically highest* role among a caller's covering grants, then asks whether that single
    role carries the permission.
  - That was sound only while every role was a superset of the one below it. `IndexMaintainer = 5`
    would outrank `Administrator = 4`, so an administrator who also held it would lose every
    administrator permission.
  - **The check must become "does any covering grant's role carry this permission".**
    `RolePermissions`' own comment already asks for this ("introducing a role that is not a superset
    … stays possible").
  - Test: a person holding both Administrator and IndexMaintainer keeps every administrator
    permission. Mutation-check it against the old max-role code.
- **Steps:**
  1. Domain, then `RolePermissions`, then the authorisation check above.
  2. Check any `Role` switch or exhaustive map. The build will find them, because warnings are
     errors.
  3. Grant and revoke validation.
  4. The Members screen role picker.
  5. Regenerate `operations.ts`.
- **Tests:**
  - the role's permission set is exactly those two;
  - an `IndexMaintainer` can run `detect_staleness` and `reindex`, and is refused `create_draft`,
    `grant_membership` and `export_project`;
  - the worker's installation shape allow-list still passes;
  - the e2e suite grants the role and runs the sweep.
- **Docs:**
  - `CLAUDE.md` ("stale-record-sweep needs ManageIndex, which only the Administrator role
    carries");
  - `docs/operations/deployment.md`;
  - ADR-0013, with an amendment note;
  - `info.md`.
- **Then enable it on the devbox** (after it ships in a tag):
  1. Create a `stale_worker` account holding `IndexMaintainer` in the one workspace.
  2. The account mints its own token.
  3. Set the token as the stale sweep's `DEVBUDDY_WORKER_TOKEN`.
  4. Run `--stale-after 90d --every 24h`. **The 90-day value is proposed; the owner confirms it
     before it is enabled.**
- **Exit:**
  - the role is in a release;
  - the devbox runs the sweep under it;
  - one pass is recorded in the audit trail under `stale_worker` with channel `InternalSystem`.

### B9 — The bounded AI scope approval flow (SB-18)
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

`ProjectAiAccessPolicy.BoundedDataScope` exists as free text, but no screen sets it. The Projects
screen enables AI access without a scope.

- **Steps:**
  1. Replace free text with a structured scope: which categories are allowed (customer,
     production, personal) plus a written justification. Stored so that an old free-text value
     still reads back.
  2. On the Projects screen, add a separate "approve a bounded scope" action:
     - Administrator only;
     - a confirmation that names each category;
     - the justification required;
     - shown on the project afterwards;
     - revocable.
  3. Audit the approval and its revocation. Their details carry the categories and never content.
  4. Confirm that the SB-18 decision reads only the approved categories. A scope allowing
     `personal` must not also let production data through.
  5. A secret stays refused inside any scope (`info.md`).
- **Tests:**
  - per category, allowed versus still redacted;
  - a secret refused inside a scope;
  - a non-administrator refused;
  - an e2e run of the approval and revocation.
- **Owner's instruction:** no scope is approved on the devbox in this phase.
- **Exit:** the flow is shipped and tested, SB-18's evidence is extended in the matrix, and no scope
  exists on the devbox.

### B10 — `linux-musl-arm64` to the verified tier
**Status: DONE (2026-09-21)**

- **Steps:**
  1. Add a per-release smoke test on Alpine arm64 on the Mac mini (`ssh macmini`, Docker via
     `public.ecr.aws`) to the checklist in `release-matrix.md`.
  2. Run it for the next release.
  3. Update ADR-0008 (amendment), the release notes template in `release.yml` (the unverified tier
     becomes `win-arm64` alone), `release-readiness.md`, `CLAUDE.md` and `info.md`.
- **Exit:** the tier change is recorded in `info.md` and the smoke test is recorded for a real
  release. `win-arm64` is untouched.

---

## C — Test coverage

`tests/e2e/README.md` "What is not covered" lists these. Each item removes its line from that
section when it is done.

### C1 — Embeddings and the workers, end to end
**Status: DONE (2026-09-21)** — on jmhp and in CI.

- A Compose override for the e2e stack only:
  - the `pgvector/pgvector:pg17` database;
  - a **deterministic stub embedder** speaking the OpenAI-compatible shape, which hashes text to a
    fixed vector, so no model is downloaded in CI;
  - both workers run with `compose run`.
- **Tests:**
  - the sweep embeds a published record and skips a draft;
  - a project closed to AI is not embedded;
  - a second pass sends nothing;
  - `search_similar_records` finds it;
  - an archived record leaves;
  - a revoked worker token is refused;
  - the stale sweep under `IndexMaintainer` (from B8);
  - the stub records what it received, and a credential never arrives.

### C2 — MCP over stdio with a machine token
**Status: DONE (2026-09-21)** — on jmhp and in CI.

- A Playwright test using the MCP SDK's stdio client, which runs
  `docker compose run --rm -T mcp --stdio` with `DEVBUDDY_TOKEN`.
- **Tests:**
  - the tool list equals the allow-list;
  - a call succeeds;
  - a revoked token is refused on the next call;
  - a token from another workspace is refused;
  - `DEVBUDDY_ACTOR` is ignored.

### C3 — GitHub API source mode
**Status: DONE (2026-09-21)** — on jmhp and in CI.

- A stub GitHub API server in the e2e stack. It serves:
  - pull requests;
  - issues;
  - review comments;
  - a pack file for one commit.
- It is run with `GitHubOptions.Mode=GitHubApi`, an allow-list entry for the stub, and private
  addresses allowed in the e2e stack only.
- **Tests:**
  - `sync_sources` reports open counts;
  - `analyze_change_impact` answers against the commit;
  - a host not on the allow-list is refused.

### C4 — SMTP delivery
**Status: DONE (2026-09-21)** — on jmhp and in CI.

- Mailpit in the e2e stack, with `Email:Provider=Smtp`.
- **Tests:**
  - an invitation arrives at the right address;
  - a recovery mail arrives, and the link completes a password reset;
  - no token appears in the container logs.

### C5 — Observability overlay
**Status: DONE (2026-09-21)** — on jmhp and in CI.

- Start the stack with `compose.observability.yaml`.
- **Tests:**
  - a trace for an operation reaches Tempo;
  - its attributes use only the allowed vocabulary, with no identifiers and no URL path;
  - logs reach Loki;
  - Grafana's security dashboard provisions.

### C6 — Restore, end to end
**Status: DONE (2026-09-21)** — on jmhp and in CI.

- Automate the drill in the e2e runner:
  1. Seed data and evidence.
  2. `backup_system`.
  3. Destroy **both** volumes.
  4. `restore`.
  5. Assert records, revisions, the approval hash binding, evidence bytes and machine tokens.
  6. After D6, assert that an access token from before the restore is refused.

### C7 — Firefox and WebKit
**Status: DONE (2026-09-21)** — on jmhp and in CI.

- Add both as Playwright projects. The setup project runs once, and the specs run per browser.
- Fix whatever differs in the client, not in the tests.

### C8 — The hosted provider mode, exercised
**Status: BLOCKED (no hosted server)** — on 2026-09-23 the owner withdrew Voyage AI and it was removed from the code (`info.md`). The hosted mode stays, for the owner's own model server on a cloud machine. A real run waits for one to exist and for its acceptance.

- A test against a recording double shaped like the named vendor (part of B6).
- **A run against the real vendor happens only after the owner's acceptance**, with synthetic data,
  and is recorded in `release-matrix.md`.

### C9 — `linux/arm64` on server-class hardware
**Status: DONE (2026-09-21)**

- A CI job on GitHub's `ubuntu-24.04-arm` runner, which is Arm Neoverse server hardware. It builds
  the arm64 images natively and runs `tests/e2e/run.sh` against them.
- First, confirm that the runner is available to this repository's plan. If it is not, record that
  and ask the owner about alternatives, such as a cloud arm64 VM.
- **Exit:** a green run on arm64 server hardware, recorded in `release-matrix.md`.

---

## D — The accepted limitations

Each item says what can honestly be done. Two of them cannot be removed, only narrowed. That is
stated rather than claimed away.

### D1 — Password recovery without SMTP
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Problem:** with no SMTP and no `Email:AllowTokensInLog`, a recovery token is generated and is
  unreachable.
- **Fix:** add a human-only operation, `issue_password_reset`. It:
  - needs Administrator on the user's workspace;
  - returns a single-use recovery token in its own response, the way `create_user_account` returns
    a setup token;
  - never writes the token to a log;
  - is audited without the token;
  - refuses a caller resetting their own account;
  - **refuses unless the caller holds `ManageAccounts` in every workspace where the subject holds
    a live membership**. A password is installation-wide. Without this rule, an administrator of
    workspace A could reset a person who also belongs to workspace B and sign in to B as them.
    This is a cross-tenant takeover, and a test must prove it is refused;
  - refuses a disabled account, as recovery already does;
  - revokes the target's sessions when the reset completes, which the recovery flow already does.
- **UI:** a "Reset password" action on the Members screen shows the link once.
- **Tests:**
  - authorisation, including self-reset refused;
  - the token is single-use and expires;
  - it is not in the logs;
  - the audit row carries no token;
  - an e2e run of an administrator resetting a user who then signs in.

### D2 — Rows stored against a foreign project
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Problem:** `scope-report` lists them and nothing removes them. `info.md` (2026-09-17) says
  deleting is the operator's call.
- **Fix:** keep that rule, and give the operator the tool: `scope-report --delete`.
  - It runs only when given the exact count it just reported (`--confirm <n>`).
  - It deletes the rows and, for evidence, the bytes in the object store.
  - It writes an audit entry per table (`InternalSystem`).
  - It is outside the pipeline, like `restore`.
  - It is never run by default and never by a migration.
- **Tests:**
  - the delete refuses a wrong or missing count;
  - it removes rows and bytes;
  - it leaves every legitimate row;
  - it is idempotent.
- Record the decision in `info.md` as an extension of the 2026-09-17 entry.

### D3 — AI drafts from before the 2026-09-15 fix
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Problem:** such a draft may be stored as a person's work, and nothing recorded the channel.
  This cannot be derived.
- **Fix:** a human-only operation, `mark_record_ai_generated`. It:
  - needs Administrator;
  - takes a reason (required);
  - works **one direction only**: it can mark a revision AI-generated and can never clear the mark;
  - is audited with the reason;
  - adds a provenance correction to history rather than editing the stored revision;
  - keeps the content hash unchanged, so an approval stays bound.
- The UI shows "marked AI-generated by \<admin\> on \<date\>: \<reason\>".
- This does not recover the truth. It lets an owner who knows record it. `info.md`'s "cannot be
  re-marked" line is amended to say exactly that.
- **Tests:**
  - it cannot unmark;
  - it needs a reason;
  - a non-administrator is refused;
  - the hash is unchanged;
  - the history shows the correction.

### D4 — Audit rows with no channel
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Constraint:** `info.md` and `CLAUDE.md` bind this. Null means "not recorded" and must never be
  backfilled with a guess. A backfill would make the audit trail less trustworthy, not more.
- **What can be done:**
  1. Verify that every reader treats null consistently, as not recorded and never as `Human`:
     - the UI;
     - `read_audit_history`;
     - the export;
     - the channel filter, including a new explicit "not recorded" filter value, so an
       investigator can list exactly the rows with no channel.
  2. Add the date range those rows span to `docs/operations`, so an investigator knows where the
     gap is.
- **Exit:**
  - the filter exists and is tested;
  - the gap is documented;
  - the limitation remains, stated as permanent by design.

### D5 — Records embedded before the Thai SB-18 rules
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Problem:** the sweep skips a record whose content hash is already indexed, so a change to the
  redaction rules never reaches it.
- **Fix:** the index key includes a **redaction rules fingerprint**, a stable hash of the SB-18
  rule set's names and versions, alongside the content hash. When the rules change, every record
  re-embeds once, within the budget, over successive passes. No migration is needed if the
  fingerprint is folded into the stored hash; decide once the code is read.
- **Tests:**
  - an unchanged rule set sends nothing;
  - a changed rule set re-embeds;
  - the budget bounds it.
- **Devbox:** the next pass after deploying re-embeds that installation's records.

### D6 — An access token still valid after a restore
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Problem:** a stateless JWT inside its 15-minute lifetime still validates after a restore,
  because the signing key is configuration and survives.
- **Fix:** bind each access token to its session with a `sid` claim, and refuse a token whose
  session row does not exist. Sessions are not restored, so every pre-restore token dies with the
  restore. This also makes sign-out and "revoke all sessions" immediate for access tokens, not only
  for refresh tokens.
- **Cost:** one lookup per request. Authorisation already reads the account and membership per
  request. Measure it, and cache per request if needed.
- **Tests:**
  - a token whose session was deleted is refused;
  - sign-out invalidates the access token at once;
  - a machine token is unaffected (it has its own row);
  - C6 asserts it across a real restore.
- Update `backup-and-restore.md` and `release-readiness.md`, which record this overstatement.

### D7 — Deleted data living on in older backups
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Problem:** a project deleted after a backup comes back if that backup is restored. This is the
  "backup lag" residual in `info.md`'s accepted limitations.
- **Fix:** a **tombstone ledger** kept beside the backups, on the same volume, so it survives the
  loss the backups are for.
  - `delete_project` and retention purges append the identifiers they removed, never content.
  - `restore` reads the ledger and skips anything tombstoned after the backup was taken, before it
    writes a row or a byte.
  - The retention sweep drops a tombstone once no backup older than it remains.
- **What remains:** a copy of a backup taken off the volume is outside this. That is stated.
- **Tests:**
  - delete after a backup, then restore, and the project stays deleted, rows and bytes;
  - the ledger holds identifiers only;
  - a restore with the ledger missing proceeds and warns rather than failing.

### D8 — Copies somebody downloaded before their access was revoked
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Why it stays:** a file that left the system cannot be recalled by it. No code changes that.
- **What can be done:** when a grant is revoked, the Members screen shows what that person
  downloaded or exported in a chosen window. This comes from the audit trail, which already records
  `download_evidence` and `export_project`. An owner then knows what to follow up outside the
  system.
- **Tests:**
  - the list is scoped to the revoking administrator's workspace;
  - it shows identifiers and titles, never content;
  - a viewer cannot see it.
- **Exit:**
  - the report exists;
  - `info.md`'s accepted limitation is reworded to say the copy cannot be recalled, and that the
    report exists.

### D9 — Ollama embeds only the first 4096 tokens
**Status: DONE (2026-09-21)** — merged to `main`; ships in the next release.

- **Problem:** a long record is embedded from its beginning only, so a match late in the record is
  invisible to semantic search.
- **Fix:** chunk the published text before embedding.
  - Chunks are bounded by a configured token estimate, with overlap, on paragraph boundaries where
    possible.
  - The index holds one row per chunk (`chunk` column, conditional migration, pgvector only).
  - `search_similar_records` ranks a record by its best chunk and still returns one hit per record.
  - The budget counts chunks, because they are texts sent.
  - SB-17 scans each chunk before it leaves.
  - Re-embedding by content hash still skips an unchanged record whole.
- **Tests:**
  - a long record yields several chunks;
  - a query matching the tail finds it;
  - one hit per record;
  - the budget counts chunks;
  - a secret in chunk 3 skips the record and sends nothing;
  - the migration is still skipped on `postgres:17-alpine`.

---

## Exit criteria for Phase 13

- Every item is `DONE`, `BLOCKED` with its reason, or `CLOSED — NOT POSSIBLE` with its reason.
- `v1.5.0` is published with its checklist complete, and the devbox runs that tag.
- `tests/e2e/README.md` "What is not covered" names only what is still out of reach, with reasons.
- `info.md` records each decision this phase took.
- `verification-matrix.md` records each row that gained evidence, and nothing moves backwards.

---

## Progress log

Newest last. Every entry records the date, the item, what was verified and where, and what was not.

| Date | Item | What happened |
|---|---|---|
| 2026-09-21 | — | Plan written and approved by the owner. B6 is blocked on the vendor. HTTPS and `win-arm64` are out of scope. |
| 2026-09-21 | A4 | **DONE.** `CLAUDE.md`'s SB-19 line now says the tests passed in CI. The claim was checked, not assumed from a green tick: the `ubuntu-latest` test results of CI run 35328802725 (`0fdc064`, 2026-09-18) were downloaded, and they show 896 .NET tests with none failed. `AuditChannelTests`, the two `LifecycleAndAuditTests` SB-19 names, the SB-18 personal-data tests and `DraftEditorReadTests` passed. The dated "Not yet run in CI" entries in `verification-matrix.md` are left as written, as history, with a new 2026-09-21 entry beside them. `docs/plan.md`'s Phase 0 line is annotated as historical. Its closing section no longer says "what is left is a caller and a schedule", and it points here. `AGENTS.md` and `CLAUDE.md` point here, with the rule to log each finished item. No build or test was run for this item, because it is documentation only. |
| 2026-09-21 | B6, A1 | The owner named **Voyage AI** as the hosted vendor, which unblocks B6 and C8, and confirmed `v1.4.0`. Both are recorded in `info.md`. |
| 2026-09-21 | B8 (code) | **`IndexMaintainer` built and tested.** It carries `ReadKnowledge`, `ManageOwnCredentials` and `ManageIndex` only. Authorization now asks whether any covering grant carries the permission, instead of picking the highest-numbered role. Under the old rule, a person holding Administrator and IndexMaintainer would have lost every administrator permission. There is a Members-screen option, and `operations.ts` was regenerated. ADR-0013 is amended, and `deployment.md` and `CLAUDE.md` are updated. **Verified on jmhp** (clean clone, SDK container): 901 .NET tests passed, 0 failed (Security 139, Application 273), `dotnet format` exit 0, web 73 pass. **Mutation-checked:** with the old max-role `AuthorizationService.cs` restored, `an_administrator_who_also_holds_index_maintainer_keeps_every_permission` fails. The new e2e test in `permissions.spec.ts` failed first, on its own mistake: `detect_staleness` needs `staleAfter`, and the request was refused as invalid with 400 before any authorization ran. With that fixed, `tests/e2e/run.sh` on jmhp passed **63 of 63**. **Not done:** the devbox enablement, which needs a release carrying the role, the owner's worker account, and the owner's confirmation of `--stale-after` (90d proposed). **Found, not fixed:** the web client's `useWorkspace` uses only a person's first grant in a workspace (`session.tsx`), so someone holding two grants there sees the navigation of whichever came first. This predates B8. It is logged as new item A5. |
| 2026-09-21 | A1 (pre-tag) | **The full pre-tag checklist passed against `826b34e`, with nothing carried over.** On jmhp: 896 .NET tests, format, 73 web tests, the `linux-x64` publish, Compose from clean, non-root, tokens out of the log both ways, and the destroy-and-restore drill with both volumes. Three new checks: a made-up project refused, `scope-report` clean, `list_source_repositories` human-only. Also the upgrade from `v1.3.0` on amd64. On the Ubuntu 26.04.1 arm64 guest: the upgrade from `v1.3.0`. CI was green on `826b34e`, all four jobs. Recorded in `release-matrix.md`. **Script gap found:** since `0fad716` the console writes refusals to stderr, so the drill script's one-line refusal captures came back empty. Each refusal was read from the stderr log instead, and the AI-operation count was recounted with `awk '$NF=="ai"'` (20, identical to `v1.3.0`). **Not yet:** the tag, the post-tag artefact checks, and the smoke tests. |
| 2026-09-21 | A1 (post-tag), A3 | **Tag `v1.4.0` pushed at `163243f`.** Release run 35562161924 was green first time and left a draft. The post-tag checklist passed and is in `release-matrix.md`: checksums, and attestations from outside the workflow (six archives and three images, wrong owner refused). SBOMs 36/38/56. Both architectures in every manifest. The published images started on amd64 (jmhp) and on arm64 (Ubuntu guest). Smoke tests: `win-x64` natively, `linux-x64`, `linux-musl-x64`, `osx-arm64` natively on the M4, `linux-arm64` natively in the arm64 guest, and `linux-musl-arm64` in Alpine. **`win-arm64` was not run and not downloaded**, on the owner's instruction. The release notes are on the draft. **A1 is not DONE:** publication waits for the owner. **A3:** `plugin.json` is `1.4.0` in the tag. `PluginPackageTests` passed in the 896-test run. Reinstalling the local plugin waits for publication. |
| 2026-09-21 | D1 | **DONE.** `issue_password_reset` is human-only and needs `ManageAccounts`. It returns a single-use recovery token in its own response, and the token is never logged or audited. The Members screen has "Reset password". **The cross-tenant rule:** the reset is refused (Denied, audited as `AccessDenied`, refused by `account-reach`) unless the caller holds `ManageAccounts` workspace-wide in *every* workspace where the subject holds a live grant, because a password works in all of them. Also refused: resetting yourself, a disabled account, and somebody outside the workspace (NotFound). New port methods: `IAccountRecoveryService.IssueForAsync` and `IAccessDirectory.ListLiveMembershipsEverywhereAsync`. New audit action: `PasswordResetIssued = 42`. **Verified on jmhp:** 908 .NET tests passed, 0 failed, including 7 in `PasswordResetTests` over real PostgreSQL; format exit 0; web 73; **e2e 65 of 65**, including the UI reset and sign-in, and the API refusal across workspaces. **Mutation-checked:** disabling the everywhere rule fails `an_administrator_of_one_workspace_cannot_reset_somebody_who_also_belongs_to_another` and `a_project_scoped_administrator_elsewhere_is_not_enough`. Along the way the first run failed on its own tests, not the product: fakes missing the new port method, a registry entry missing, and an audit query without the `operation:` prefix. `operations.ts` regenerated, and the Thai handbook rebuilt (63 operations). `CLAUDE.md` and `release-readiness.md` updated. **Not in a release yet.** |
| 2026-09-21 | D2 | **DONE.** `scope-report --delete --confirm <count> --actor <id>` performs the same deletion as `delete_project`, keyed on workspace and project together, so another tenant's project is never touched. Evidence bytes go too. It is refused, with nothing changed, if the count moved since the report or if the actor does not administer (workspace-wide `ManageProjects`) every workspace involved. Each purged project is audited as `StrayScopeRowsPurged` (43) on `InternalSystem`. The `info.md` 2026-09-17 entry is extended; the report-don't-migrate decision stands. **Test** `the_scope_report_deletes_only_when_the_count_and_the_actor_are_right` (real PostgreSQL): wrong count refused, non-admin refused, strays and bytes gone, legitimate rows and the other tenant's project intact, 2 audit rows, second run nothing to purge. **Mutation-checked:** disabling the administrator check fails it. |
| 2026-09-21 | D3 | **DONE.** `mark_record_ai_generated`: human-only, `ManageProjects`, reason required (500 max). Every unmarked revision gains `Provenance.AiMarking` (who, when, why), stored in the provenance JSON with no migration. One way: refused when every revision is already AI, and never overwritten. Content and content hash are untouched, so approvals stay bound. It is the only change `UpdateRecordAsync` accepts on a stored revision. The record page shows the marking and offers the action; `get_record` returns `aiMarking`, redacted on the AI channel. **Tests:** 4 in `AiMarkingTests` over real PostgreSQL, plus a domain test. **Mutation-checked:** not persisting the mark fails two of them. |
| 2026-09-21 | D4 | **DONE.** `read_audit_history` takes `channelNotRecorded`, and it is refused together with a channel (Invalid). The Audit screen offers "Channel not recorded (before v1.3.0)". `deployment.md` explains where such rows are and why they are never backfilled. The limitation stays by design (`info.md` 2026-09-15). **Test:** the existing legacy-row test also asserts the new filter returns exactly the null-channel rows and refuses the contradiction. **Mutation-checked:** ignoring the filter fails it. |
| 2026-09-21 | D5 | **DONE.** The embedding sweep keys index rows on `contentHash:ruleSetFingerprint`. The fingerprint is SHA-256 over `PersonalDataRules.ChecksVersion` and each rule's name, pattern and options, in order, exposed as `IPersonalDataRedactor.RuleSetFingerprint`. Rows written before carry the bare hash, so each record re-embeds once, within budget. `ChecksVersion` must be bumped when an accept check changes without its pattern changing. **Tests:** a sweep test (stale key → re-embed with new key; same rules → nothing sent) and a fingerprint test (stable; changes with pattern, order, version). **Mutation-checked:** ignoring the fingerprint fails the sweep test. **Found while writing it:** a static initialiser declared above `All` would have hashed an empty rule set, so it is computed on read. **Devbox:** the next pass after deploying re-embeds its records; not yet deployed. |
| 2026-09-21 | D2–D5 verification | One run on jmhp of the branch carrying all four: **916 .NET tests passed, 0 failed**, format exit 0, web 73, **e2e 65 of 65**. The four mutation checks were run separately, because the e2e runner consumed the rest of a piped script the first time and the D3 mutant silently did not run. `operations.ts` was regenerated and the Thai handbook rebuilt. |
| 2026-09-21 | D6 | **DONE.** Each access token carries a `devbuddy_session` claim naming its refresh-token family. The API and the MCP server's HTTP transport refuse a token whose family has no live refresh token (`SessionTokenCheck` in each host, because ASP.NET's event model must not reach Infrastructure). This covers sign-out, revoke-all and restore. **Tests:** `SessionBoundTokenTests` (4); an e2e test that a signed-out token is refused on the MCP transport; the restore stage asserts a pre-disaster token gets 401. **Mutation-checked:** removing the check fails 3 of 4. Tokens issued before this carry no session, so everyone signs in once after upgrading. |
| 2026-09-21 | D7 | **DONE.** `ProjectDirectory.DeleteProjectAsync` appends identifiers to `deletions.jsonl` in the backup root. `restore` deletes again every project recorded after the backup was taken, bytes included, and says how many. A missing ledger is reported, not fatal. Retention prunes the ledger to the oldest backup left. **Tests:** 3 in `RestoreDrillTests` over real PostgreSQL and MinIO. **Mutation-checked:** not re-applying fails the main one. **Found on the way:** the first build failed on CA1848 (a direct `LogWarning`), so it uses a `LoggerMessage` delegate. |
| 2026-09-21 | D8 | **DONE.** `list_member_downloads` is human-only and needs `ReadAudit`. It lists one person's succeeded `EvidenceDownloaded` and `ExportCreated` entries in this workspace only: identifiers and times, never content. The Members screen offers it beside Revoke. The limitation itself stays: a copy cannot be recalled. **Tests:** 2 in `MemberDownloadsTests`. **Mutation-checked:** dropping the workspace bound fails. |
| 2026-09-21 | D9 | **DONE.** `TextChunks.Split` makes chunks of `Embedding:ChunkCharacters` (3000), cut on paragraph, line or space, with a tenth's overlap. All chunks go in one gateway call. The index keeps a row per chunk: migration `RecordEmbeddingChunks`, conditional, **the ninth migration**. A similarity query ranks a record by its best chunk. **Tests:** splitter (3), sweep, and index over pgvector. **Mutation-checked:** ranking by the worst chunk fails. The first run failed on two mistakes in my own tests, not the product: a repeating Thai text, and asserting calls instead of texts. |
| 2026-09-21 | B9 | **DONE.** A bounded scope names rules from `PersonalDataRuleNames.All` and needs a justification of 20 to 1000 characters. Only those rules are let through on the AI channel; every other rule is blocked and redacted, and a secret is refused regardless. Free text is refused for new approvals; a stored one is honoured as before and flagged on the Projects screen, which approves, changes and withdraws a scope behind a confirmation naming each rule. The scanner has rules, not customer/production categories, so the scope is expressed in rules (`info.md` notes it). **Tests:** a real-pipeline test (email allowed; Thai ID and mobile blocked and redacted; secret blocked), and a parity test holding the scanner's rules equal to the list. **Mutation-checked.** No scope was approved on the devbox. |
| 2026-09-21 | A5 | **DONE.** `mergeAccess` in `session.tsx` combines grants: workspace-wide ones at workspace level, and those plus the project's own inside a project. Web tests: 4. Its first version failed the build on a strict-index error, now fixed. |
| 2026-09-21 | B6 | **DONE (adapter).** `Embedding:Dialect=VoyageAi` sends `input_type`: document for the sweep, query for search, through a new `EmbeddingPurpose` on the port. `deployment.md` has the settings; the allow-list entry goes in `compose.override.yaml` on purpose, and the owner sets the key. **Tests:** `VoyageEmbeddingTests` (4) against a Voyage-shaped double: `input_type`, bearer key, allow-list refusal, and a vendor refusal quoting neither the key nor the text. **Mutation-checked:** sending query as document fails. **Not done:** enabling it anywhere. |
| 2026-09-21 | B7 (code) | `embedding-check`: a read-only console command. It reports the provider, egress, the vector index, the worker token's reach and the budget, and exits 3 on a problem. `docs/operations/embedding-approval.md` has the checks it cannot make, the `info.md` template and the rollback. **Tests:** `EmbeddingReadinessTests` (5). **Mutation-checked.** **Left:** run it on the devbox and record the output. |
| 2026-09-21 | B10 | **DONE.** `linux-musl-arm64` is in the verified tier: its smoke test ran for `v1.2.0`, `v1.2.1`, `v1.3.0` and `v1.4.0`. ADR-0008 is amended, and `release-matrix.md`, `release-readiness.md`, `CLAUDE.md` and `info.md` are updated. `win-arm64` is the only unverified RID. |
| 2026-09-21 | C1–C7 | **DONE on jmhp.** The last full run (tip minus a one-line test fix and docs): **default suite 205 of 205 across Chromium, Firefox and WebKit (C7)**, plus two host stages. **Stdio (C2):** 8 of 8 — token minted, 20 tools, no human-only tool, a call succeeds, refused in another workspace and after revocation, `DEVBUDDY_ACTOR` alone resolves nobody. **Restore (C6):** 6 of 6 — both volumes destroyed; accounts, records, revisions, evidence rows, one artefact's bytes and channelled audit entries back; a pre-disaster token refused. **Mailpit (C4)** is in the default stack: 3 specs. **Embeddings mode (C1):** 2 of 2 plus setup — the real sweep as a Viewer at a one-minute interval, the draft and the closed project never reach the model, an archived record leaves the answers. **GitHub mode (C3):** 2 of 2 plus setup, on a second API instance. **Observability mode (C5):** 3 of 3 plus setup. CI gains three jobs, one per mode. |
| 2026-09-21 | Defect (found by C2/C6) | **Every console `run` and every worker pass failed since D1** was merged to `main` (not in any release, not on the devbox). `issue_password_reset` → recovery service → `TokenService`, whose constructor refused a missing signing key, and the console carries none. The key is now checked when a token is signed. `ConsoleCompositionTests` composes every operation without a key and checks that a short key is still refused at signing. **Mutation-checked:** an eager check fails both. |
| 2026-09-21 | Defect (found by C5) | **No log line ever reached Loki on a stack with its file log on**, which is every stack `docker/compose.yaml` starts. Serilog owned the pipeline, so the OpenTelemetry exporter registered after it received nothing. Option 2 of `logging.md`, in force since 2026-09-10, never delivered. Serilog now writes to the other providers. `FileLoggingForwardingTests` holds it. **Mutation-checked.** Recorded in `info.md` and `logging.md`. |
| 2026-09-21 | Verification | Branch tip on jmhp: **944 .NET tests passed, 0 failed**, format exit 0, web 77. Every item's mutation check was caught; the D6 and key checks were first written wrong and re-run until they applied. **Not yet:** these CI jobs on GitHub, the arm64 runner (C9), and a release carrying any of this. |
| 2026-09-21 | C9, CI | **DONE.** CI run 35578883536 on `main` at `8b03a58` passed every job. That includes **End-to-end (Playwright, ubuntu-24.04-arm)**, the whole stack built natively for `linux/arm64` on GitHub's Arm server runner, the first arm64 run on server-class hardware. The other jobs that passed: e2e on x64 (three browsers, stdio and restore stages), the embeddings, GitHub source and observability modes, build and test on Linux and Windows, and the format check. Supply chain run 35593315251 passed too. C1–C7 are therefore verified in CI as well as on jmhp. |
| 2026-09-22 | A1 | **DONE.** `v1.4.0` published from `163243f` at the owner's instruction, 02:27 UTC, and GitHub reports it as Latest. The checklist was already recorded in `release-matrix.md`. Nothing changed after that record. |
| 2026-09-22 | A2 | **DONE.** On the devbox: backup `backup-20260922-022901-92cb396edef748dbb` taken first, and copied with `.env` to `/data/devbuddy-cache/backups/before-v140-20260922/`. `/data/devbuddy` checked out at `v1.4.0`; `git describe` reports `v1.4.0`. The source under `src` and `docker` is identical to `3e7cd47`, which it ran before, so no migration ran. Images were rebuilt, then `api`, `mcp`, `migrate`, `retention`, `evidence` and `record-embedding-sweep` recreated. **Probes:** `migrate` exited 0; `/health` 200; `/` 200; MCP `POST` 401; `scope-report` found nothing (exit 0). The application services run as uid 1654, the evidence store as 1000. **Worker pass:** as its Viewer account; embedded 0, skipped 3 (the project's three drafts), removed 0. |
| 2026-09-22 | A3 | **DONE.** The installed package in `~/.devbuddy/claude-marketplace` was byte-identical to the `v1.3.0` tag's `plugins/claude`, with no local edits. It was replaced by the `v1.4.0` tag's, which differs only in the version. `claude plugin update devbuddy` reports 1.3.0 → 1.4.0. **Checks:** `list_projects` through the plugin returned `test_project`, and `search_similar_records` answered "Nothing is indexed". **Caveat:** that call went through the session's already-running MCP container. A new session starts one from the tag's image. |
| 2026-09-22 | B7 | **DONE.** `v1.4.0` does not carry `embedding-check`: it is on `main` for `v1.5.0`. So it ran from a console image built from `main`, `b7a4af5`, used for this one run and deleted afterwards. The command ran in the sweep's own service settings, token included, against the live devbox database: `docker compose --profile workers run --rm --no-deps record-embedding-sweep embedding-check --budget 50`. It changed nothing and sent no text. The running worker was not recreated. **Output, exit 0:**<br>`ok provider SelfHosted, model qwen3-embedding:0.6b, 1024 dimensions, dialect OpenAiCompatible`<br>`ok vector index present (pgvector)`<br>`ok worker token valid; its owner holds Viewer`<br>`ok budget 50 text(s) per pass`<br>This matches the devbox entry in `info.md` of 2026-09-16. |
| 2026-09-22 | Defect (e2e, flaky) | CI run 35602268050 on `ff0c9a5` failed one test on the arm64 runner: Firefox, `operations.spec.ts:19`. The trace shows the page loaded and both fields filled, but no `POST /auth/sign-in` ever left the page, and the form showed no error. The click was taken and nothing was submitted. The product was not at fault: every other sign-in in the run, across three browsers and both runners, went through. `signIn` in `tests/e2e/support/fixtures.ts` now counts a click only once the request is seen, and fills and clicks again if it is not (up to 30 seconds). A second sign-in only opens a second session. **Verified on jmhp:** 205 of 205 tests, .NET suite and format, web 77. |
| 2026-09-22 | Defect (found by e2e in CI) | **A write made while a list was loading for the first time was never shown.** CI run 35680015949, chromium: a project was created, `create_project` answered 200, but the Projects list stayed without it. The trace shows the page's first `list_projects` still in flight when the create finished, and no second read. TanStack Query's `invalidateQueries` restarts a fetch in flight only when the query already holds data, so it handed back the fetch that had read the list before the write. **All 20 call sites** on eight screens now go through `refetchAfterWrite` (`web/admin/src/api/queries.ts`), which cancels, then invalidates. **Tests:** `test/queries.test.ts` reproduces the race. **Mutation-checked:** without the cancel it fails. The same run's Firefox failure on the recovery form looked like the sign-in fault of 35602268050, but its trace was lost to an artifact upload refusal (403), so that match is by appearance only. `submitUntilSent` now covers both signed-out forms. The cause of the lost click is **not identified**. **Verified:** .NET 944 on jmhp, format, web 78. The e2e suite did not run on jmhp because its root filesystem was full (below). CI run 35681047131 on `ada3914` passed every job, both e2e runners included. |
| 2026-09-22 | jmhp disk | The e2e image build on jmhp failed with `no space left on device`. `/` (98 GB) was at 100%, because containerd's image store and build cache live in `/var/lib/containerd` on `/` (75 GB), not under Docker's `data-root` on `/data`. Freed by removing only what this phase's verification left: the throwaway `devbuddy-e2e-*` images; one leftover embeddings-mode e2e container, running for 18 hours because its teardown missed a profile service; the `devbuddy-v140` and `devbuddy-up140` checklist stacks, stopped with **volumes kept**; and unused build cache. Left at 5.5 GB free (95%). The live `devbuddy` stack stayed healthy throughout, and nothing of any other project was touched. **The owner's to decide:** moving containerd's root onto `/data`, or growing `/`. Until then, one full e2e run on jmhp takes most of what is free. |
| 2026-09-22 | Defect (e2e teardown) | `run.sh`'s teardown ran `compose down` with no profile. That leaves a profile service running, holding its network and volume: the embeddings mode's `record-embedding-sweep` ran on jmhp for 18 hours after its run. The teardown now names the `e2e` and `workers` profiles, the only two. **Verified** with a two-service Compose file on jmhp: after a plain `down` the profile service was still running, and after `--profile workers down` nothing was left. The full embeddings mode has not been re-run on jmhp since (disk, above). |
| 2026-09-22 | Flaky e2e (Firefox) | CI run 35681490953 on `6abd332` failed three more Firefox tests, all on signed-out forms: setting a password (its trace shows the page reloaded, the fields filled and no `recovery/complete` sent), a sign-in after an administrator's reset, and the lockout test. The arm64 runner's report upload was refused again (403), so those two have no trace. The fault is systematic in Firefox, not a one-off: the first submit on a freshly loaded signed-out page sometimes sends nothing. **Cause not identified**, and not reproduced by hand. `clickUntilSent` in `support/fixtures.ts` replaces `submitUntilSent`, and every signed-out submit that should send a request goes through it. The client-side password-mismatch check is left alone, because it sends nothing by design. The click is repeated only when nothing left within five seconds. **Open question for a person with Firefox:** whether a quick first click on the sign-in page is ever lost by hand. The tests cannot tell a browser-automation fault from a product one. |
| 2026-09-22 | CI | Run 35682074421 on `96c511e` passed every job twice, attempt 2 re-run on purpose because the fault is intermittent: both e2e runners (205 tests each, three browsers), the three mode jobs, both build-and-test jobs and format. |
| 2026-09-22 | v1.5.0 pre-tag | The owner chose to release `v1.5.0` before B8 on the devbox (`info.md`, same day), and the Claude plugin was bumped to 1.5.0. The full pre-tag checklist ran against `50ccaaa` and passed. **Tests:** .NET 944, format, web 78, CI green. **Compose from clean:** nine migrations. **Drill:** both volumes destroyed; a project deleted after the backup stayed deleted; an access token from before the disaster was refused with 401. **Tokens:** kept out of the log. **Upgrade from published `v1.4.0`:** on amd64 on jmhp, and on arm64 in the Ubuntu guest. **New checks:** the stale-record sweep ran as an `IndexMaintainer` and was refused for a Viewer; an administrator-issued reset worked; AI marking left the hash unchanged. `release-matrix.md` has every row. The e2e suite is taken from CI run 35682074421, not re-run on jmhp (disk). |
| 2026-09-22 | v1.5.0 published | Tag `v1.5.0` pushed from `cb818a3`; release run 35685837379 passed every job on the first attempt. **Post-tag checklist, all passed:** checksums, and attestations from outside the workflow for six archives and three images, with a wrong-owner control refused; SBOMs 36/38/56; both architectures under one digest per tag. **Published images started:** on amd64 (jmhp) and arm64 (Ubuntu guest), nine migrations. **Smoke tests:** `win-x64` natively, `linux-x64`, `linux-musl-x64`, `osx-arm64` natively on the M4, `linux-arm64` natively in the guest, and `linux-musl-arm64` in Alpine there. **Published** 04:22 UTC as Latest, at the owner's instruction. **devbox:** backup `backup-20260922-042257-b7da0b6098f741a29` and `.env` copied to `/data/devbuddy-cache/backups/before-v150-20260922/`. Checked out at `v1.5.0`, rebuilt and recreated. `RecordEmbeddingChunks` applied (nine migrations, `chunk` column present); `/health` 200, MCP `POST` 401, `scope-report` clean, the worker pass ran, `embedding-check` all ok, every service non-root. **Plugin 1.5.0** installed, and `list_projects` answered through it. The session's MCP container predates the upgrade until a restart. **B8 is next, on the owner's side.** |
| 2026-09-22 | B8 | **DONE.** The owner created an account holding only the `IndexMaintainer` role, workspace-scoped, and minted its token `stale_worker`, expiring 2027-09-22. The owner set `DEVBUDDY_STALE_SWEEP_TOKEN` and `DEVBUDDY_STALE_AFTER=365d` and started `stale-record-sweep`. **Checked on the devbox:** the service runs as uid 1654 with `worker stale-record-sweep --every 24h --stale-after 365d`. Its first pass completed: `test_project`, 0 stale records, nothing refused. The token that pass used belongs to an account whose only live membership is role 5 (`IndexMaintainer`) on the workspace. The approval is recorded in `info.md`, and it replaces "no stale sweep" of 2026-09-16. |
| 2026-09-22 | `win-arm64` retrospective | At the owner's instruction, every release previously skipped for `win-arm64` was backfilled. Published `v1.0.0`, `v1.1.0`, `v1.4.0` and `v1.5.0` archives matched `SHA256SUMS`, their PE apphosts were ARM64 (`0xAA64`), their operation catalogues matched (18, 18, 20 and 20), stderr was empty and exit 0, and the applicable invalid-interval checks exited 2. `migrate` without a connection string reached Infrastructure's refusal and exited 2. Sigstore provenance was verified against the repository, release workflow, tag ref and source commit; a wrong-owner control was refused. Together with the releases already run, every published release has now passed on the Windows on ARM VMware guest. The tier is unchanged: no future per-release obligation was inferred. |
| 2026-09-23 | C8, B6 | **Voyage withdrawn; C8 stays BLOCKED, now for want of a hosted server.** At the owner's instruction Voyage AI is removed from the code: `EmbeddingDialect`, `DEVBUDDY_EMBEDDING_DIALECT`, `input_type` and `EmbeddingPurpose`. It was never enabled anywhere. The model server stays Ollama or LM Studio and may be on another machine: `SelfHosted` for the stack or the LAN, `HostedApi` for a cloud machine. `SelfHosted` now refuses a public address, a literal at start-up and a name before every call with nothing sent (`SelfHostedEndpoint`, `EmbeddingEndpointTests`). `deployment.md`, `embedding-approval.md` and `info.md` are updated. **Verified on jmhp** at `a02385d`: .NET 958 passed and none failed, format clean, web 78, and the generated client unchanged. **Not run:** the e2e suite, because jmhp's root filesystem is at 97%, and CI, because the branch is not pushed. |
| 2026-09-24 | C8, B6 | **The Voyage withdrawal, merged to `main` and run end to end.** The owner approved the merge and freed space on jmhp. The merged `main` at `746c7c4` ran there: .NET 958 with none failed, format clean, web 78, and the generated client unchanged. The Playwright suite passed 205 of 205. In the embeddings mode it passed 211, with the stand-in model server reached by its Compose name. That name resolved through Docker's DNS to a private address, so the `SelfHosted` check let the real sweep and search through. The `devbuddy-e2e-*` images were removed afterwards. **CI:** pushed at the owner's instruction. Run 35947885621 on `73a5a6e` passed all eight jobs: format, build and test on Ubuntu and Windows, both Playwright runners (amd64 and arm64), and the embeddings, GitHub and observability modes. Supply chain run 35947885600 passed too. |
| 2026-09-24 | v1.6.0 pre-tag | The owner asked for a release after the Voyage withdrawal (`info.md`, same day). The Claude plugin was bumped to 1.6.0. The full pre-tag checklist ran against `ac2a117` and passed. **Tests:** .NET 958, format, web 78, CI run 35954269823 green. **Compose from clean:** nine migrations, no service as root, tokens kept out of the log. **Drill:** both volumes destroyed and every row came back. **New checks:** a public self-hosted literal was refused at start-up, a private name and a LAN literal started, and a leftover dialect setting was ignored. **Upgrade from published `v1.5.0`:** on amd64 on jmhp, and on arm64 in the Ubuntu guest; no manual step, and nobody signs in again. `release-matrix.md` has every row. |
