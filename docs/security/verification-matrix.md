# Verification Matrix

This file is the **single source of truth for whether a security control actually works**. A row
moves out of `NOT IMPLEMENTED` only when the named test exists, runs in CI, and passes. Nothing
else — not a design document, not a code review, not an intention — changes a row.

**Current state: 0 of 33 controls implemented. 0 verified.**

Controls are defined in [security-baseline.md](security-baseline.md); threats in
[threat-model.md](threat-model.md).

## Status values

| Value | Meaning |
|---|---|
| `NOT IMPLEMENTED` | No code exists for this control. |
| `IMPLEMENTED` | Code exists. **Not proven.** Never report a control as done at this status. |
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
| SB-05 | Path traversal and symlink escape rejected | 6 | Path-traversal corpus | NOT IMPLEMENTED | — |
| SB-06 | SSRF prevention incl. redirects and DNS | 6 | SSRF corpus | NOT IMPLEMENTED | — |
| SB-07 | MCP tool allow-list, both transports | 7 | Tool-surface equality test per transport | NOT IMPLEMENTED | — |
| SB-08 | AI access denied by default per project | 7 | AI-disabled project returns nothing via MCP | NOT IMPLEMENTED | — |
| SB-09 | Results narrowed to requesting user | 7 | Two-user differential MCP query | NOT IMPLEMENTED | — |
| SB-10 | Human-gated operations absent from AI surface | 7 | Absence test | NOT IMPLEMENTED | — |
| SB-11 | Server-side authorization every request | 4 | Cross-user/team/project access tests | NOT IMPLEMENTED | — |
| SB-12 | Isolation covers search, exports, attachments, caches | 4 | Isolation suite over each path | NOT IMPLEMENTED | — |
| SB-13 | Password storage, lockout, rate limiting | 4 | Lockout and rate-limit tests | NOT IMPLEMENTED | — |
| SB-14 | Token lifetime, rotation, revocation | 4 | Revoked-token and rotation-replay tests | NOT IMPLEMENTED | — |
| SB-15 | Safe account recovery | 4 | Reuse, expiry, and enumeration tests | NOT IMPLEMENTED | — |
| SB-16 | Role enforcement server-side | 4, 8 | Role matrix test per endpoint | NOT IMPLEMENTED | — |
| SB-17 | Secret detection and redaction, retention and egress | 6 | Secret corpus at both points | NOT IMPLEMENTED | — |
| SB-18 | Customer/production/personal data denied by default | 6, 7 | Policy test incl. approved bounded scope | NOT IMPLEMENTED | — |
| SB-19 | Audit stores no sensitive payload | 5 | Audit content test | NOT IMPLEMENTED | — |
| SB-20 | Provenance and history support correction | 1, 5 | Provenance invariant and correction flow | NOT IMPLEMENTED | — |
| SB-21 | Size, rate, and concurrency limits | 10 | Oversized upload, flood, concurrency tests | NOT IMPLEMENTED | — |
| SB-22 | Analysis execution time limit | 6 | Pathological input test | NOT IMPLEMENTED | — |
| SB-23 | Approval bound to exact revision | 1, 5 | Domain invariant + stale-approval e2e | NOT IMPLEMENTED | — |
| SB-24 | Revisions immutable | 1, 3 | Immutability tests, domain and persistence | NOT IMPLEMENTED | — |
| SB-25 | Provenance mandatory, snapshots retained | 1, 3 | Invariant + snapshot round-trip | NOT IMPLEMENTED | — |
| SB-26 | Drafts separated from published everywhere | 5, 7, 8 | Draft-visibility test across API, MCP, UI | NOT IMPLEMENTED | — |
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

**None of the eight has been exercised.**

---

## Change log

| Date | Change |
|---|---|
| 2026-09-01 | Created in Phase 0. All 33 rows `NOT IMPLEMENTED`; no scenario exercised. |
