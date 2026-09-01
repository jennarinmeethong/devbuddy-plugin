# ADR-0010: DevBuddy owns the work item, GitHub is read-only

- Status: Accepted
- Date: 2026-09-01
- Phase: 6

## Context

`info.md` requires work identity to be retained (identifier, type, title, goal, scope, exclusions,
related repositories and modules, stakeholders), requires `sync_sources` to import authorised source
systems, and requires links and snapshot metadata for Git, issues, pull requests, and commits so
knowledge can be verified against its origin. It does not say whether work items originate here or
in GitHub. The project owner confirmed the recommendation on 2026-09-01.

## Decision

**DevBuddy is the source of truth for the work item** and every knowledge record derived from it.

GitHub issues, pull requests, and commits are imported **read-only** as linked references plus
snapshot metadata. Nothing is ever written back to GitHub in v1. `sync_sources` is therefore
one-way: fetch, snapshot, refresh, and flag drift between a snapshot and its origin. There is no
bidirectional sync and no conflict-resolution engine in scope.

Work item types are spelled out as `develop`, `enhance`, `fixbug`, `change_request`, and
`code_review`. Change Request and Code Review are separate concepts and separate record types, and
the abbreviation `CR` is not used as a shared identifier anywhere in code, schema, API, or UI.

## Consequences

- The knowledge system holds context that no issue tracker records: rationale, alternatives,
  handover notes, root causes, and review reasoning. Owning the work item is what lets those hang
  off a single identity.
- One-way sync removes an entire class of failure: no write-back conflicts, no accidental mutation
  of a customer issue tracker, and a much smaller GitHub token scope.
- Drift between a snapshot and its origin is surfaced rather than resolved automatically. A human
  decides what a divergence means.
- Cost: a work item exists in two places when a team also tracks it in GitHub. Accepted; the link
  and snapshot make the relationship explicit.

## Alternatives considered

- **GitHub Issues as the source of truth.** No place to store rationale, decisions, or handover
  content, and it would make the system unusable for repositories hosted elsewhere.
- **Bidirectional sync.** Needs conflict resolution, a broader token scope, and write access to a
  system we do not own. Rejected for v1.
