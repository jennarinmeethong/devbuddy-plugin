# ADR-0011: Persistence maps explicit row types, not the aggregates

- Status: Accepted
- Date: 2026-09-01
- Phase: 3

## Context

The domain aggregates enforce their invariants through private constructors, private collections,
and value objects. Mapping them onto EF Core directly is possible, and it is what most projects
do, but it costs something specific: EF needs a way to materialise an aggregate, which in practice
means adding a private parameterless constructor to every entity. That constructor produces a
record with no revision, no provenance, and no scope, and it exists solely so a framework can fill
the properties in afterwards.

`info.md` says the point of this system is knowledge a later owner can trust, and `DevBuddy.Domain`
already carries an architecture test proving it references nothing. The ports were also designed
around explicit saves (`AddRecordAsync`, `UpdateRecordAsync`) rather than an ambient unit of work.

## Decision

The schema is a set of plain `*Row` classes in `DevBuddy.Infrastructure.Persistence`, and
`RowMappers` translates between them and the aggregates.

The domain gains exactly one persistence seam: `KnowledgeRecord.Rehydrate`. It is the only way to
produce a record that is already approved, published, or archived without replaying its history,
and it re-checks every structural invariant on the way in — revisions contiguous from 1, a
published revision that exists, an archived record with an archive time, approvals pointing at
revisions that are present. A database that has drifted fails at load rather than at publish.

Every other aggregate is rebuilt through its real public API: a `Membership` is granted and then
revoked, an `EvidenceObject` is constructed and then given its scan result. No back door was
needed for those.

## Consequences

- No aggregate has a constructor that leaves it half-built, so the Phase 1 invariants hold for
  code paths that did not exist when they were written.
- The schema is legible on its own. Someone reading `Rows.cs` and the migration knows what is in
  the database without inferring it from mapping configuration.
- `Rehydrate` turned out to be a control rather than a hole: an integration test corrupts a row to
  claim a published revision that does not exist, and the load throws.
- The cost is real and concentrated in `RowMappers`: roughly 400 lines that have to stay in step
  with the aggregates. A field added to an aggregate and forgotten here is a silent data loss, and
  the round-trip tests are what catch it.
- No change tracking of aggregates. `UpdateRecordAsync` reads the stored graph and appends, which
  is also where the storage-layer immutability check lives.

## Alternatives considered

- **Map the aggregates directly.** Around 40 percent less code. It requires a parameterless
  constructor on thirteen types, each of which weakens exactly the invariant that type exists to
  hold. Rejected on that ground.
- **Replay the history to rebuild an aggregate.** Attractive, because it proves the stored state is
  reachable through legal transitions. It cannot reproduce intermediate timestamps, and it would
  re-run current validation against old data, so a tightened rule would make historical records
  unloadable. Rejected.
- **A document store, one JSON blob per aggregate.** Simplest mapping of all, and it gives up the
  relational queries that tenant isolation, search, and audit all depend on. Rejected.
