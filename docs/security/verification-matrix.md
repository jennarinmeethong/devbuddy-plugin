# Verification Matrix

This file is the **single source of truth for whether a security control actually works**. A row
moves out of `NOT IMPLEMENTED` only when the named test exists, runs in CI, and passes. Nothing
else — not a design document, not a code review, not an intention — changes a row.

**Current state: 33 controls. 24 `TESTED`, 1 `IMPLEMENTED` but unproven, 8 `NOT IMPLEMENTED`.**

Controls are defined in [security-baseline.md](security-baseline.md); threats in
[threat-model.md](threat-model.md).

## Status values

| Value | Meaning |
|---|---|
| `NOT IMPLEMENTED` | No code exists for this control. |
| `IMPLEMENTED` | Code exists, or part of the verification passes. **Not proven.** Never report a control as done at this status. The Evidence column says exactly which part passes and which is outstanding. |
| `TESTED` | An automated test or a recorded drill exercises the control and passes in CI. |
| `GAP` | Implemented but the verification failed or was skipped. Must be stated in release notes. |

## Rules

1. Only a passing test or a recorded drill moves a row to `TESTED`.
2. If a test is deleted, disabled, or quarantined, the row returns to `GAP` in the same commit.
3. A release note lists every row that is not `TESTED`, verbatim. Silence is not permitted.
4. `docs/plan.md` phase exit criteria reference this file; a phase does not complete while a
   control it claimed is below `TESTED`.

---

## Matrix

| ID | Control (short) | Phase | Verification | Status | Evidence |
|---|---|---|---|---|---|
| SB-01 | Untrusted content handled as data | 6 | Injection corpus test | **TESTED** | `PromptInjectionCorpusTests` analyses a repository containing exfiltration instructions, hidden HTML comments addressed to an assistant, and a metadata-service URL. All of it comes back as text, and the AI tool surface is byte-identical afterwards. |
| SB-02 | Permissions enforced in code, not prompts | 6, 7 | Tool-surface test without instruction files | **TESTED** | The injection corpus compares the AI-exposed operation list before and after analysing hostile content and finds it unchanged. Instruction text is not an input to any decision the system makes. |
| SB-03 | Network destination allow-list | 6 | Egress test | **TESTED** | `UrlGuardTests`: nothing is reachable with an empty allow-list, which is the default. The analyser has no HTTP client at all, and the corpus proves a URL found in a document is reported rather than fetched. |
| SB-04 | No repository execution | 6 | Hostile-hooks repository, process-spawn assertion | **TESTED** | Two ways. Behaviourally: a fixture with a build script, a Makefile, an npm preinstall hook, and an MSBuild pre-build target, each of which would leave a marker file; every analysis kind runs and the marker never appears. Structurally: `NoExecutionTests` scans the product source and fails the build if any process or dynamic-loading API appears. |
| SB-05 | Path traversal and symlink escape rejected | 3, 6 | Path-traversal corpus | **TESTED** | `PathGuardTests` covers relative escapes, absolute paths outside the root, a null byte, a sibling directory sharing the root prefix, and a symlink pointing out. Resolution happens before comparison, which is what catches the link. |
| SB-06 | SSRF prevention incl. redirects and DNS | 6 | SSRF corpus | **TESTED** | `UrlGuardTests`: loopback, link-local, the cloud metadata address, private and carrier-grade ranges, IPv6 forms, an IPv4-mapped loopback, non-HTTP schemes, and a host resolving to both a public and a private address. Every resolved address is checked, not only the first. |
| SB-07 | MCP tool allow-list, both transports | 2, 7 | Tool-surface equality test per transport | **TESTED** | `ToolSurfaceTests` asserts the exported tool list *equals* the eighteen-name allow-list — not contains, not excludes — against a list written out independently of the catalogue, so a change has to be made twice. The two transports are covered by one test rather than two: a source scan fails the build if anything outside `ToolSurface.cs` constructs a `Tool`, which is what makes a per-transport list impossible rather than merely absent. |
| SB-08 | AI access denied by default per project | 4, 7 | AI-disabled project returns nothing via MCP | **TESTED** | The named verification, exactly: `CrossSurfaceTests.ai_access_off_returns_nothing_to_mcp_while_a_human_reads_the_same_record` reads one record over real HTTP as an authorised person and gets it, calls the same tool on the AI channel against the same container and is refused, then enables the policy and gets it. The third step is what makes the second evidence rather than a broken path. |
| SB-09 | Results narrowed to requesting user | 4, 7 | Two-user differential MCP query | **TESTED** | `with_ai_access_on_two_callers_get_different_answers_through_mcp`: with the project opted in, a member reads the record through the MCP handlers, and an account with no membership calls the same tool with the same arguments and is refused. AI access is a project opt-in, never a bypass of the permissions of the person asking. |
| SB-10 | Human-gated operations absent from AI surface | 2, 7 | Absence test | **TESTED** | Absent, and refused if named anyway. `no_human_gated_operation_appears_on_the_surface` checks every Denied operation against the exported list; `calling_a_human_gated_operation_by_name_is_answered_as_unknown` shows `publish_record` gets the same answer as an operation nobody defined, with no mention of permission; and `a_human_only_operation_is_refused_on_the_ai_channel_even_with_ai_access_on` shows that a workspace administrator on the AI channel is still on the AI channel. |
| SB-11 | Server-side authorization every request | 2, 4 | Cross-user/team/project access tests | **TESTED** | `TenantIsolationTests` and `RevokedAccessAndRoleTests` run the real pipeline over the real authorization service against PostgreSQL: cross-user, cross-team, cross-project, and disabled-account cases all denied. |
| SB-12 | Isolation covers search, exports, attachments, caches | 3, 4 | Isolation suite over each path | **TESTED** | All four paths covered end to end in `TenantIsolationTests`: record read, search, export, and attachment download. There is no cache layer yet; if one is added this row returns to `IMPLEMENTED` until it is covered too. |
| SB-13 | Password storage, lockout, rate limiting | 4, 7 | Lockout and rate-limit tests | **TESTED** | All three. Hashing and lockout in `AccountSecurityTests`, and over HTTP in `repeated_wrong_passwords_lock_the_account_out`, including that the right password does not lift a lockout. Rate limiting in `the_authentication_endpoints_are_rate_limited`, which runs a second instance of the real application with a small budget and sees the excess refused with 429. The limiter is partitioned by remote address, so exhausting it does not lock everybody else out. |
| SB-14 | Token lifetime, rotation, revocation | 4 | Revoked-token and rotation-replay tests | **TESTED** | `AccountSecurityTests`: rotation, reuse detection revoking the whole family, single revoke, revoke-all, and expiry. Refresh tokens are stored only as a hash, and a test asserts that. The same behaviour is exercised over HTTP in `AuthenticationEndpointTests`. **Residual risk, added in Phase 8:** the server issues bearer tokens rather than setting an HttpOnly cookie, so the web client holds a refresh token in `sessionStorage`, where any script that reaches the page can read it. Bounded to one tab and cleared when it closes; the fix is a cookie-based refresh, and that is a server change. |
| SB-15 | Safe account recovery | 4 | Reuse, expiry, and enumeration tests | **TESTED** | `AccountSecurityTests`: single-use, expiry, a new request retiring the previous token, no account enumeration, and recovery signing every existing session out. |
| SB-16 | Role enforcement server-side | 4, 7, 8 | Role matrix test per endpoint | **TESTED** | The exhaustive role-by-permission matrix runs against the real pipeline (`RolePermissionsTests`, `RevokedAccessAndRoleTests`), including that a project grant does not reach a workspace-level operation. Per endpoint collapses to per operation here on purpose: the API has one dispatch route rather than forty hand-written ones, so there is no endpoint that could hold a different opinion. `a_viewer_cannot_reach_the_approval_or_audit_surfaces` drives a real viewer session over HTTP and is refused approval, correction, publication, archiving, audit reading, membership listing, project creation, and account creation. The UI hides those, and the smoke test checks that it does; the refusal is the server's either way. |
| SB-17 | Secret detection and redaction, retention and egress | 2, 6 | Secret corpus at both points | **TESTED** | A real scanner and redactor sharing one rule set. `SecretCorpusTests` covers eight secret shapes plus negatives that must survive untouched; `RetentionAndEgressTests` proves a draft carrying a credential is refused with nothing stored, that every retained field is scanned, and that a secret already in the database is redacted on the way out. Accepted limitation AL-2 still applies: this catches known shapes and high entropy, not everything. |
| SB-18 | Customer/production/personal data denied by default | 6, 7 | Policy test incl. approved bounded scope | NOT IMPLEMENTED | — |
| SB-19 | Audit stores no sensitive payload | 2, 5 | Audit content test | **TESTED** | `LifecycleAndAuditTests.the_audit_trail_never_contains_the_content_it_describes` scans every audit row and detail column in real PostgreSQL for a marker that is present in the record body. Mutation-checked: adding the marker to an audit detail makes it fail. Detail values are capped at 200 characters in the domain and refused rather than truncated. |
| SB-20 | Provenance and history support correction | 1, 5 | Provenance invariant and correction flow | **TESTED** | Provenance is mandatory (`RecordRevisionTests`) and the correction flow runs end to end against real PostgreSQL (`a_record_travels_from_draft_through_correction_to_published`), with the reason retained on the stored record. |
| SB-21 | Size, rate, and concurrency limits | 3, 6, 10 | Oversized upload, flood, concurrency tests | IMPLEMENTED | Evidence size (`an_oversized_object_is_refused_and_nothing_is_written`), the analysis file and size ceilings (`AnalysisLimitsTests`), and now an authentication rate limit tested over HTTP. **Rate limiting covers the credential endpoints only, and there is no concurrent-job cap at all**; both arrive in Phase 10. |
| SB-22 | Analysis execution time limit | 6 | Pathological input test | **TESTED** | `AnalysisLimitsTests`: a spent budget returns a report saying the answer is incomplete rather than throwing, a caller cancellation still propagates, the file ceiling bounds the walk, an oversized file is listed but not read, and a 60-deep tree finishes. A non-positive timeout means no time, not unlimited. |
| SB-23 | Approval bound to exact revision | 1, 5 | Domain invariant + stale-approval e2e | **TESTED** | Both halves pass. End to end against real PostgreSQL: publishing after the text changed is rejected and audited, and approving a hash that is no longer current is rejected and audited. |
| SB-24 | Revisions immutable | 1, 3 | Immutability tests, domain and persistence | **TESTED** | Both halves pass against real PostgreSQL: `RecordRevisionTests` and `PersistenceTests.rewriting_a_stored_revision_is_refused`. |
| SB-25 | Provenance mandatory, snapshots retained | 1, 3 | Invariant + snapshot round-trip | **TESTED** | Invariant and jsonb round-trip both pass: `RecordRevisionTests`, `PersistenceTests.a_published_record_round_trips_with_its_whole_history`. |
| SB-26 | Drafts separated from published everywhere | 1, 5, 7, 8 | Draft-visibility test across API, MCP, UI | **TESTED** | Three places. At the pipeline: `a_reader_of_published_knowledge_never_sees_the_draft_that_follows_it`. Over HTTP: `a_draft_written_after_publication_is_not_what_a_reader_gets` writes an unapproved revision on top of a published record and shows the reader still gets the approved text, with the listing naming which revision is live. On the AI channel: `an_unapproved_revision_is_not_served_to_the_ai_channel_either`, which is the surface that matters most for it. The UI is covered by construction rather than by a fourth test — `get_record` is its only source for a record body, so it cannot show what that operation will not serve. |
| SB-27 | Retention applies to all data copies | 11 | Purge tests per copy + deleted-project sweep | NOT IMPLEMENTED | — |
| SB-28 | Dependencies pinned and scanned | 0, 10 | CI vulnerability gate | NOT IMPLEMENTED | — |
| SB-29 | SBOM and verifiable artifact origin | 10 | SBOM attached per release | NOT IMPLEMENTED | — |
| SB-30 | Database and object store not publicly exposed | 10 | Compose port-exposure test | NOT IMPLEMENTED | — |
| SB-31 | Non-root containers, no Docker socket | 10 | Container inspection test | NOT IMPLEMENTED | — |
| SB-32 | Secrets never in images or the repository | 0, 10 | Repository and image secret scan | NOT IMPLEMENTED | — |
| SB-33 | Backup and tested restore | 10 | Destroy-and-restore drill | NOT IMPLEMENTED | — |

---

## Phase 11 scenario coverage

The eight scenarios `info.md` requires, and the controls that must be `TESTED` before each is
considered exercised.

| # | Scenario | Controls |
|---|---|---|
| 1 | Cross-user, team, and project access incl. search, caches, attachments, exports | SB-11, SB-12 — **exercised** |
| 2 | Revoked permissions take effect | SB-14, SB-11 — **exercised** |
| 3 | Malicious source and tool content | SB-01 to SB-07 — **exercised** |
| 4 | Sensitive-data leakage | SB-17, SB-19 and SB-26 **exercised**; SB-18 not implemented |
| 5 | Approval revision mismatch | SB-23, SB-24 — **exercised** |
| 6 | Resource exhaustion | SB-21, SB-22 |
| 7 | Recovery | SB-33 |
| 8 | Retention and deletion across copies | SB-27, SB-32 |

**Four of the eight are fully exercised, and one more is most of the way there.** Scenarios 1, 2, 3 and 5 run against real PostgreSQL through the real pipeline; scenario 3 completed when the MCP tool surface arrived in Phase 7. Scenario 4 is exercised for detection and audit; SB-18, the customer and production data policy, is not implemented. Scenarios 6, 7 and 8 need concurrency limits, backups, and retention (Phases 10 and 11).

---

## Change log

| Date | Change |
|---|---|
| 2026-09-01 | Created in Phase 0. All 33 rows `NOT IMPLEMENTED`; no scenario exercised. |
| 2026-09-01 | Phase 1. SB-20, SB-23, SB-24, SB-25, SB-26 moved to `IMPLEMENTED`: the domain half of each is tested and passing. **Nothing is `TESTED`.** Scenario 5 (approval revision mismatch) is exercised at the domain level only. |
| 2026-09-01 | Phase 2. SB-07, SB-10, SB-11, SB-17, SB-19 moved to `IMPLEMENTED`: the application-layer half of each is tested against fake ports. **Nothing is `TESTED`.** No control has been exercised against a real database, a real MCP transport, or a real secret scanner. |
| 2026-09-01 | Phase 3. **First two rows reach `TESTED`:** SB-24 and SB-25 pass end to end against real PostgreSQL, including the storage-layer refusal to rewrite a stored revision. SB-05, SB-12 and SB-21 move to `IMPLEMENTED` for the parts persistence covers. Still untested: everything needing authentication, an MCP transport, or a secret scanner. |
| 2026-09-01 | Phase 4. SB-11, SB-12, SB-14 and SB-15 reach `TESTED`; SB-08, SB-09, SB-13 and SB-16 move to `IMPLEMENTED`. **Scenarios 1 and 2 are exercised.** SB-17 is downgraded in substance rather than status: the redactor that ships today redacts nothing, and the note now says so. |
| 2026-09-01 | Phase 5. SB-19, SB-20 and SB-23 reach `TESTED`, taking the total to nine. **Scenario 5 is exercised.** Audit entries now carry structured, capped metadata, so the approver, the exact approved revision, the timestamp, and whether the approver wrote the draft are in the audit history rather than only on the record. |
| 2026-09-01 | Phase 6. Seven rows reach `TESTED` — SB-01, SB-02, SB-03, SB-04, SB-05, SB-06 and SB-17 — taking the total to sixteen, more than half. **Scenario 3 is exercised except for SB-07.** The placeholder redactor is gone: a real scanner and redactor now share one rule set, and the pipeline refuses inbound content carrying a credential rather than storing it. Source synchronisation was not delivered; `sync_sources`, `compare_snapshots` and `analyze_change_impact` throw until an adapter exists. |
| 2026-09-01 | Phase 6 debt closed. SB-22 reaches `TESTED`, taking the total to seventeen. Source synchronisation now works against a mounted working copy, read as files rather than by running git; packed objects are reported as unsupported by name rather than as absent history. |
| 2026-09-02 | Phase 7. Six rows reach `TESTED` — SB-07, SB-08, SB-09, SB-10, SB-13 and SB-16 — taking the total to twenty-three. **Scenario 3 is now fully exercised.** The MCP tool surface exists and is pinned to the allow-list, and a cross-surface test reads one record as an authorised human over HTTP while the same call on the AI channel is refused until the project owner opts in. SB-21 and SB-26 stay `IMPLEMENTED`: rate limiting covers only the credential endpoints, there is no concurrency cap, and no test yet asserts draft invisibility through a specific surface. |
| 2026-09-03 | Phase 8. SB-26 reaches `TESTED`, taking the total to twenty-four; only SB-21 remains `IMPLEMENTED` but unproven. A draft written on top of a published record is proved invisible over HTTP and on the AI channel, and a viewer session is refused approval, correction, publication, archiving, audit reading, membership listing, and every provisioning operation. SB-14 gains a residual risk it did not have before there was a browser: the web client holds a refresh token in `sessionStorage`, because the server issues bearer tokens rather than setting a cookie. |
