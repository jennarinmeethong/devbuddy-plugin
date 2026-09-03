# Release Readiness

Phase 11 exit criteria, from `docs/plan.md`: the verification matrix has no silent gaps, and every
remaining risk is named and accepted explicitly by the project owner before real project data is
connected. This note is that naming. `docs/security/verification-matrix.md` is the per-control
source of truth; this note does not repeat its 33 rows, only the ones that are not `TESTED` and
the eight Phase 11 scenarios.

## Status

**31 of 33 controls are `TESTED`. 2 are `IMPLEMENTED` but not fully proven, by name below. 0 are
`NOT IMPLEMENTED`.**

## The two controls not at `TESTED`

### SB-27 — Retention applies to all data copies

**Implemented and tested:** audit events, orphaned evidence, and backups. `RetentionEnforcementTests`
proves all three against real PostgreSQL and the real filesystem — an item past its window is
actually gone from its copy, not only reported as due. Run from `dotnet run -- retention`, an
operator-scheduled console command outside the pipeline, on whatever cadence the deployment's own
cron or task scheduler provides; this system starts no scheduler of its own.

**Residual risk, not implemented, named for explicit owner acceptance:**

- **Exports carry no stored artefact.** `ExportAsync` returns a manifest — record and evidence
  counts, a reference, an expiry — but writes nothing to disk or object storage. There is
  currently nothing for a purge to remove. Accepting this residual risk means accepting that the
  30-day export expiry in the retention schedule is aspirational until an export actually
  materialises a copy somewhere.
- **Application log retention is outside this codebase.** Logging goes to stdout via the default
  `Microsoft.Extensions.Logging` console provider; retention is a property of whatever collects
  container output (the Docker log driver, or a downstream aggregator), not application code.
  `docker/compose.yaml` sets a size-bounded `json-file` log driver as an operational floor, which
  bounds disk growth but does not implement the 90-day window from the schedule.
- **A deleted-project sweep has no trigger.** No operation in this build deletes a project at all —
  `info.md`'s v1 scope has no project-deletion use case — so there is nothing for the sweep to run
  from. This is not a gap in the purge logic; it is a capability that does not exist yet.

Draft staleness is not a gap: it is a report (`DetectStaleness`, Phase 6), not a deletion, and the
schedule's "12 months untouched, then flagged stale" is already what that control does.

### SB-29 — SBOM and verifiable artifact origin

Unchanged from Phase 10: SBOMs are generated per host and uploaded as build artifacts, but no
release has carried one, and nothing is signed or attested. Moves to `TESTED` when a release
actually ships one.

## Phase 11 scenario coverage

| # | Scenario | Status |
|---|---|---|
| 1 | Cross-user, team, and project access, incl. search, caches, attachments, exports | Exercised |
| 2 | Revoked permissions take effect immediately | Exercised |
| 3 | Malicious source and tool content produces no unauthorized action | Exercised |
| 4 | Sensitive-data leakage: secrets and personal data blocked before retention and egress | Exercised |
| 5 | Approval revision mismatch is rejected | Exercised |
| 6 | Resource exhaustion limits | Exercised |
| 7 | Backup, restore, and incident response | Exercised |
| 8 | Retention and deletion across copies | Exercised for audit events, evidence, and backups; residual risk above |

Seven of eight scenarios are fully exercised. The eighth is exercised for every copy this build
actually makes a durable one of; the three residual items above are named rather than hidden, and
none of them is a defect in the code that ships — each is a capability (a materialised export, a
project-deletion operation, an in-repo log retention mechanism) that does not exist to have a gap
in.

## Before real project data is connected

Per `info.md`, this is the explicit sign-off point. The project owner should accept, in writing:

1. Exports are a manifest, not yet a durable copy — until that changes, nothing in an export
   needs a retention sweep because nothing in it persists past the response.
2. Application log retention is an operator responsibility (log-driver or aggregator
   configuration), not a tested application behaviour.
3. A project cannot be deleted in v1, so no deleted-project purge can be exercised until project
   deletion is built.
4. No release has yet been signed or carries an attached SBOM (SB-29).

None of the four blocks the controls that are `TESTED` today, including the personal-data policy
(SB-18) and the AI-channel and tenant-isolation controls that gate access to whatever data a
workspace holds.
