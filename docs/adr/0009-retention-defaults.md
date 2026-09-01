# ADR-0009: Retention defaults across every data copy

- Status: Accepted
- Date: 2026-09-01
- Phase: 11

## Context

`info.md` requires that the data policy apply to logs, backups, exports, caches, and embeddings,
and that retention and deletion procedures be defined for those copies, not only the primary
database. It requires the procedures but does not fix the durations. The project owner confirmed
the recommended schedule on 2026-09-01.

## Decision

Ship the following defaults, configurable per deployment. Each has a scheduled purge job and a test
that proves the data is actually gone from that copy.

| Data | Default retention |
|---|---|
| Published record revisions | Indefinite; superseded revisions kept, never rewritten |
| Archived records | 24 months after archive, then eligible for deletion on owner request |
| Draft records | 12 months untouched, then flagged stale, never auto-deleted |
| Evidence objects | Life of the linked record plus 90 days; orphans purged weekly |
| Audit events | 24 months |
| Application logs | 90 days, redacted at write time |
| Exports | 30 days, object purged and links invalidated |
| Backups | Daily kept 30 days; monthly kept 12 months |
| Search index and caches | Derived and rebuildable; cache TTL at most 15 minutes; invalidated immediately on permission change |
| Deleted project | Hard purge within 30 days across database, evidence, exports, and caches |

Deleted data ages out of backups on the backup schedule rather than immediately. That lag is
recorded as residual risk AL-5 in the threat model and stated in release notes. It is not hidden.

## Consequences

- Retention is enforced by jobs with tests, so control SB-27 is verifiable rather than aspirational.
- Drafts are flagged rather than deleted, because silently destroying unfinished work would
  contradict the purpose of the system.
- Audit at 24 months bounds growth while covering a realistic investigation window.
- Backup lag means a deletion request is not fully honoured until the backup window passes. Stated
  explicitly to the requester.

## Alternatives considered

- **Indefinite retention everywhere.** Simplest, and it makes every future breach worse. Rejected.
- **Purging backups on deletion.** Would compromise restore integrity for a marginal gain. Rejected
  in favour of disclosing the lag.
