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

## Amendment — 2026-09-10: what "scheduled" means

The decision above says each row "has a scheduled purge job". Through v1 the *purge* was real and
tested and the *schedule* was not: `dotnet run -- retention` applied every row, and nothing ran it.
An operator who never built a scheduler had windows enforced on paper and swept never, which is the
one shape this project set out to avoid. Phase 12B closed it, and the shape of the fix was
constrained by two things this system already decided.

**A scheduling mode on the console, not a `cron` sidecar.** The three images are chiseled — no
shell, no package manager, nothing in them to execute — so a sidecar running cron would mean
building a shell-bearing image for the purpose and putting it back into a stack made that way on
purpose. `retention --every 24h` keeps the schedule inside the one image that already carries the
sweep, and `docker/compose.yaml` runs it as a service.

**It stays outside the use-case pipeline, and this is the part that is not negotiable.** A pass
spans every workspace and every project, so there is no caller to authorise it against — the same
reason `restore_system` was deleted as an operation rather than fixed. The loop acquires no actor,
no membership and no tenant context. A scheduler that acquired one would be the installation-wide
superuser this system has deliberately never had, and it would be a superuser running unattended,
which is worse than one a person has to invoke.

Three details worth recording because they were decided rather than defaulted:

- **The first pass is immediate.** A container started with `--every 24h` that waited a day would
  leave an operator unable to tell a working schedule from a broken one until the next morning.
- **A failed pass is reported and the loop continues.** The database being briefly unreachable is
  the ordinary case for a process meant to run for months, and a scheduler that exited on the first
  restart would leave the window unswept until somebody noticed the container had stopped.
- **A scope per pass, not one for the process.** The unit of work behind the sweep is a
  `DbContext`, and a scheduler holding one open for months would hold its change tracker and its
  connection just as long.

Intervals under a minute are refused, and a bare number is refused rather than read as days —
`TimeSpan.Parse("24")` means twenty-four days, so `--every 24` would have looked configured and
swept monthly.

## Consequences

- Retention is enforced by jobs with tests, so control SB-27 is verifiable rather than aspirational.
- Since 2026-09-10 it is also *scheduled* by the shipped stack rather than by the operator, which
  is what makes the durations above descriptions of behaviour rather than of intent.
- Drafts are flagged rather than deleted, because silently destroying unfinished work would
  contradict the purpose of the system.
- Audit at 24 months bounds growth while covering a realistic investigation window.
- Backup lag means a deletion request is not fully honoured until the backup window passes. Stated
  explicitly to the requester.

## Alternatives considered

- **Indefinite retention everywhere.** Simplest, and it makes every future breach worse. Rejected.
- **Purging backups on deletion.** Would compromise restore integrity for a marginal gain. Rejected
  in favour of disclosing the lag.
