# Architecture Decision Records

One file per decision, numbered in order, never renumbered. A superseded ADR keeps its file and
gains a `Superseded by` line; it is not deleted, because the reason a decision changed is itself
knowledge worth keeping.

`info.md` remains the record of what the project owner confirmed. An ADR records *why* a
particular technical route satisfies that confirmation, and what it costs.

## Template

```markdown
# ADR-NNNN: <title>

- Status: Proposed | Accepted | Superseded by ADR-NNNN
- Date: YYYY-MM-DD
- Phase: <plan phase this decision affects>

## Context
What forces the decision. Cite the relevant line of `info.md` or `docs/plan.md`.

## Decision
The choice, stated plainly.

## Consequences
What becomes easy, what becomes hard, and what we now have to maintain.

## Alternatives considered
Each with the reason it was not chosen.
```

## Index

| ADR | Title | Status |
|---|---|---|
| [0001](0001-mcp-first-clean-architecture.md) | MCP-first architecture with one shared core | Accepted |
| [0002](0002-dotnet-10-backend.md) | .NET 10 for core, console, API, and MCP server | Accepted |
| [0003](0003-postgresql-source-of-truth.md) | PostgreSQL as source of truth, full-text search, no embeddings | Accepted |
| [0004](0004-minio-evidence-store.md) | MinIO as the default evidence store | Accepted |
| [0005](0005-product-owned-identity.md) | Product-owned identity, no external identity provider | Accepted |
| [0006](0006-mcp-dual-transport.md) | MCP ships stdio and authenticated HTTP, one allow-list | Accepted |
| [0007](0007-web-admin-ui-stack.md) | React admin UI, no web chat | Accepted |
| [0008](0008-packaging-and-release-matrix.md) | Self-contained publish tiers and Docker packaging | Accepted |
| [0009](0009-retention-defaults.md) | Retention defaults across every data copy | Accepted |
| [0010](0010-work-item-source-of-truth.md) | DevBuddy owns the work item, GitHub is read-only | Accepted |
| [0011](0011-explicit-persistence-rows.md) | Persistence maps explicit row types, not the aggregates | Accepted |
| [0012](0012-embeddings-and-vector-search.md) | Embeddings and vector search | Accepted |
| [0013](0013-knowledge-ai-worker.md) | The knowledge AI worker, and what authorises it | Accepted |

The last two are Phase 12C, confirmed in `info.md` on 2026-09-10. Both were written before any
implementation deliberately — the alternative is deciding the authorization model of a background
job while already halfway through building one.

**ADR-0012 was amended the day it was accepted.** The provider it deliberately left blank is
settled as a port with two modes, off by default — a self-hosted model, or a hosted API. Building
both adapters is unblocked; **switching the hosted one on in a deployment is a separate decision
each time**, needing the vendor named, an `OutboundAccess:AllowedHosts` entry, and an acceptance of
its own, because that mode is a path out of the boundary the 2026-09-10 acceptance does not cover.
ADR-0013 has no such gate and its authorization skeleton is built.
