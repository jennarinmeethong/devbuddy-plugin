# Verification Matrix

This file is the **single source of truth for whether a security control actually works**. A row
moves out of `NOT IMPLEMENTED` only when the named test exists, runs in CI, and passes. Nothing
else — not a design document, not a code review, not an intention — changes a row.

**Current state: 33 controls. 2 `TESTED`, 11 `IMPLEMENTED` but unproven, 20 `NOT IMPLEMENTED`.**

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
| SB-01 | Untrusted content handled as data | 6 | Injection corpus test | NOT IMPLEMENTED | — |
| SB-02 | Permissions enforced in code, not prompts | 6, 7 | Tool-surface test without instruction files | NOT IMPLEMENTED | — |
| SB-03 | Network destination allow-list | 6 | Egress test | NOT IMPLEMENTED | — |
| SB-04 | No repository execution | 6 | Hostile-hooks repository, process-spawn assertion | NOT IMPLEMENTED | — |
| SB-05 | Path traversal and symlink escape rejected | 3, 6 | Path-traversal corpus | IMPLEMENTED | The evidence filesystem store refuses an escaping key (`the_filesystem_fallback_round_trips_and_refuses_a_path_that_escapes`). The analyser corpus is Phase 6. |
| SB-06 | SSRF prevention incl. redirects and DNS | 6 | SSRF corpus | NOT IMPLEMENTED | — |
| SB-07 | MCP tool allow-list, both transports | 2, 7 | Tool-surface equality test per transport | IMPLEMENTED | The allow-list exists as `UseCaseCatalog.AiExposed` and is pinned by `UseCaseCatalogTests`. The MCP server and its two transports do not exist yet. |
| SB-08 | AI access denied by default per project | 7 | AI-disabled project returns nothing via MCP | NOT IMPLEMENTED | — |
| SB-09 | Results narrowed to requesting user | 7 | Two-user differential MCP query | NOT IMPLEMENTED | — |
| SB-10 | Human-gated operations absent from AI surface | 2, 7 | Absence test | IMPLEMENTED | `UseCaseCatalogTests` asserts each human-gated operation is Denied, and `AuthorizationEnforcementTests` proves the AI channel is refused before authorization. Absence from an actual tool list is Phase 7. |
| SB-11 | Server-side authorization every request | 2, 4 | Cross-user/team/project access tests | IMPLEMENTED | The enforcement point is tested: `AuthorizationEnforcementTests` drives all 40 use cases and proves none executes without an allow decision. The membership check itself, and the cross-tenant tests, are Phase 4. |
| SB-12 | Isolation covers search, exports, attachments, caches | 3, 4 | Isolation suite over each path | IMPLEMENTED | Records, search, and evidence are covered against real PostgreSQL (`PersistenceTests`, `SearchTests`, `EvidenceStoreTests`), including identical text in two projects. Exports and caches do not exist yet. |
| SB-13 | Password storage, lockout, rate limiting | 4 | Lockout and rate-limit tests | NOT IMPLEMENTED | — |
| SB-14 | Token lifetime, rotation, revocation | 4 | Revoked-token and rotation-replay tests | NOT IMPLEMENTED | — |
| SB-15 | Safe account recovery | 4 | Reuse, expiry, and enumeration tests | NOT IMPLEMENTED | — |
| SB-16 | Role enforcement server-side | 4, 8 | Role matrix test per endpoint | NOT IMPLEMENTED | — |
| SB-17 | Secret detection and redaction, retention and egress | 2, 6 | Secret corpus at both points | IMPLEMENTED | The egress stage exists and is tested (`PipelineTests`), including that identifiers survive it. The scanner rules and the retention-point scan are Phase 6. |
| SB-18 | Customer/production/personal data denied by default | 6, 7 | Policy test incl. approved bounded scope | NOT IMPLEMENTED | — |
| SB-19 | Audit stores no sensitive payload | 2, 5 | Audit content test | IMPLEMENTED | `PipelineTests` asserts the audit entry names the operation and resource and carries no content. Against fakes; the real audit store is Phase 3. |
| SB-20 | Provenance and history support correction | 1, 5 | Provenance invariant and correction flow | IMPLEMENTED | Domain half passes: `RecordRevisionTests`, `KnowledgeRecordApprovalTests`. Correction flow end to end pending Phase 5. |
| SB-21 | Size, rate, and concurrency limits | 3, 10 | Oversized upload, flood, concurrency tests | IMPLEMENTED | The evidence size limit is enforced and tested (`an_oversized_object_is_refused_and_nothing_is_written`). Request rate and concurrency limits are Phase 10. |
| SB-22 | Analysis execution time limit | 6 | Pathological input test | NOT IMPLEMENTED | — |
| SB-23 | Approval bound to exact revision | 1, 5 | Domain invariant + stale-approval e2e | IMPLEMENTED | Domain half passes: `KnowledgeRecordApprovalTests`. End-to-end attempt pending Phase 5. |
| SB-24 | Revisions immutable | 1, 3 | Immutability tests, domain and persistence | **TESTED** | Both halves pass against real PostgreSQL: `RecordRevisionTests` and `PersistenceTests.rewriting_a_stored_revision_is_refused`. |
| SB-25 | Provenance mandatory, snapshots retained | 1, 3 | Invariant + snapshot round-trip | **TESTED** | Invariant and jsonb round-trip both pass: `RecordRevisionTests`, `PersistenceTests.a_published_record_round_trips_with_its_whole_history`. |
| SB-26 | Drafts separated from published everywhere | 1, 5, 7, 8 | Draft-visibility test across API, MCP, UI | IMPLEMENTED | Domain keeps the published revision live while a new draft exists: `KnowledgeRecordApprovalTests`. API, MCP, and UI pending. |
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
| 1 | Cross-user, team, and project access incl. search, caches, attachments, exports | SB-11, SB-12 |
| 2 | Revoked permissions take effect | SB-14, SB-11 |
| 3 | Malicious source and tool content | SB-01 to SB-06, SB-07 |
| 4 | Sensitive-data leakage | SB-17, SB-18, SB-19 |
| 5 | Approval revision mismatch | SB-23, SB-24 |
| 6 | Resource exhaustion | SB-21, SB-22 |
| 7 | Recovery | SB-33 |
| 8 | Retention and deletion across copies | SB-27, SB-32 |

**None of the eight has been exercised end to end.** Scenario 1 is partly covered at the storage layer: cross-project reads, searches, and evidence listings are tested against real PostgreSQL. It is not complete until authentication exists and the same checks run cross-user and cross-team through the API and MCP.

---

## Change log

| Date | Change |
|---|---|
| 2026-09-01 | Created in Phase 0. All 33 rows `NOT IMPLEMENTED`; no scenario exercised. |
| 2026-09-01 | Phase 1. SB-20, SB-23, SB-24, SB-25, SB-26 moved to `IMPLEMENTED`: the domain half of each is tested and passing. **Nothing is `TESTED`.** Scenario 5 (approval revision mismatch) is exercised at the domain level only. |
| 2026-09-01 | Phase 2. SB-07, SB-10, SB-11, SB-17, SB-19 moved to `IMPLEMENTED`: the application-layer half of each is tested against fake ports. **Nothing is `TESTED`.** No control has been exercised against a real database, a real MCP transport, or a real secret scanner. |
| 2026-09-01 | Phase 3. **First two rows reach `TESTED`:** SB-24 and SB-25 pass end to end against real PostgreSQL, including the storage-layer refusal to rewrite a stored revision. SB-05, SB-12 and SB-21 move to `IMPLEMENTED` for the parts persistence covers. Still untested: everything needing authentication, an MCP transport, or a secret scanner. |
