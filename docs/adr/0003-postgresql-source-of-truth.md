# ADR-0003: PostgreSQL as source of truth, full-text search, no embeddings

- Status: Accepted
- Date: 2026-09-01
- Phase: 3

## Context

`info.md` requires a hybrid storage design with PostgreSQL as the source of truth for structured
records and their relationships, PostgreSQL in both development and production, no SQLite as an
application database in the initial scope, human-readable Markdown with front matter linked to its
database record, full-text search and structured filters in the first release, and embeddings
deferred until a provider is separately approved.

## Decision

EF Core with Npgsql. Migrations live in `DevBuddy.Infrastructure`. Search uses a generated
`tsvector` column with a GIN index, combined with structured filters. Markdown bodies with front
matter are stored alongside the structured fields of the revision they belong to. Composite indexes
lead with `(WorkspaceId, ProjectId)` so tenant isolation is also the fast path. Tests run against a
real PostgreSQL instance through Testcontainers.

No vector column, no embedding pipeline, and no vector dependency enters the solution in v1. If one
is approved later it is a derived index that can be dropped and rebuilt, never a source of truth.

## Consequences

- No provider-abstraction tax: PostgreSQL-specific features such as `tsvector` can be used directly.
- Tests need a container runtime. Accepted, because an in-memory or SQLite substitute would not
  exercise the isolation and search behaviour that matters most.
- Full-text search will miss semantic matches that embeddings would find. That is the stated
  trade-off in `info.md`, not an oversight.

## Alternatives considered

- **SQLite for development.** Rejected explicitly by `info.md`, and it would let migrations and
  search behaviour diverge between development and production.
- **A separate search engine.** More capability, another service to secure, back up, and isolate.
  Deferred.
