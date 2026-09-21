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
**Status: IN PROGRESS**

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
**Status: TODO** (after A1)

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
**Status: TODO** (with A1)

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
**Status: TODO** (found 2026-09-21 while doing B8)

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
**Status: TODO** — vendor: **Voyage AI** (owner, 2026-09-21)

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
**Status: TODO**

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
**Status: IN PROGRESS** — code merged (see log). What remains: shipping it in a tag, then the owner's
account and `--stale-after` value on the devbox.

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
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO**

- A Playwright test using the MCP SDK's stdio client, which runs
  `docker compose run --rm -T mcp --stdio` with `DEVBUDDY_TOKEN`.
- **Tests:**
  - the tool list equals the allow-list;
  - a call succeeds;
  - a revoked token is refused on the next call;
  - a token from another workspace is refused;
  - `DEVBUDDY_ACTOR` is ignored.

### C3 — GitHub API source mode
**Status: TODO**

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
**Status: TODO**

- Mailpit in the e2e stack, with `Email:Provider=Smtp`.
- **Tests:**
  - an invitation arrives at the right address;
  - a recovery mail arrives, and the link completes a password reset;
  - no token appears in the container logs.

### C5 — Observability overlay
**Status: TODO**

- Start the stack with `compose.observability.yaml`.
- **Tests:**
  - a trace for an operation reaches Tempo;
  - its attributes use only the allowed vocabulary, with no identifiers and no URL path;
  - logs reach Loki;
  - Grafana's security dashboard provisions.

### C6 — Restore, end to end
**Status: TODO**

- Automate the drill in the e2e runner:
  1. Seed data and evidence.
  2. `backup_system`.
  3. Destroy **both** volumes.
  4. `restore`.
  5. Assert records, revisions, the approval hash binding, evidence bytes and machine tokens.
  6. After D6, assert that an access token from before the restore is refused.

### C7 — Firefox and WebKit
**Status: TODO**

- Add both as Playwright projects. The setup project runs once, and the specs run per browser.
- Fix whatever differs in the client, not in the tests.

### C8 — The hosted provider mode, exercised
**Status: TODO** (after B6)

- A test against a recording double shaped like the named vendor (part of B6).
- **A run against the real vendor happens only after the owner's acceptance**, with synthetic data,
  and is recorded in `release-matrix.md`.

### C9 — `linux/arm64` on server-class hardware
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO**

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
**Status: TODO** (narrowed; the limitation itself is **not removable**)

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
**Status: TODO**

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
