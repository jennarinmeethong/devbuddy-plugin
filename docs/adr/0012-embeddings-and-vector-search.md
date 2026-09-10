# ADR-0012: Embeddings and vector search

- Status: **Proposed** — not approved, not confirmed in `info.md`, and no code exists
- Date: 2026-09-10
- Phase: 12C

## Context

`info.md` under Knowledge Storage: "Defer embeddings and vector search until an embedding
model/provider is selected and separately approved. A future vector index will be a derived search
index, never the source of truth." Under AI Usage: "Require separate approval to enable a future AI
worker or embedding provider. Permission to use Claude/Codex does not automatically authorise
another provider or background processing."

So this is two approvals, not one — a provider, and the capability. Neither exists today, and this
ADR is written to be the thing that is approved or rejected rather than to record that anything was
decided. `docs/plan.md` Phase 12C names the design problems it has to settle first; this is the
attempt at settling them, and it deliberately leaves the provider blank because that is the
project owner's to name.

v1 ships PostgreSQL full-text search and structured filters (ADR-0003), which cover exact and
lexical retrieval and do not cover "find the record that says this in different words". That gap is
the whole case for this, and it is a real one for a knowledge system whose entire purpose is a
later owner asking questions nobody anticipated the phrasing of.

## Decision proposed

**Embedding text is egress.** This is the part that has to be settled before anything else, because
it changes what the security baseline covers. Sending a record's content to an embedding provider
is a third path out of the trust boundary, alongside the AI channel and the telemetry exporter, and
it is the only one of the three that leaves a copy in somebody else's system by design.

Therefore:

1. **SB-17 and SB-18 apply before text leaves, in the same pipeline they already run in.** A
   secret is refused rather than embedded. Customer, production, and personal data is refused
   unless the project's AI access policy carries an approved bounded scope, exactly as on the AI
   channel — and a secret stays refused even inside an approved scope, per `info.md`. An embedding
   request that bypasses the pipeline is the same defect as an operation that bypasses it, and the
   architecture tests should treat it that way.
2. **The verification matrix gains its own rows.** SB-17 and SB-18 are `TESTED` against the AI
   channel and a draft; they are not tested against an embedding call that does not exist. Reading
   the existing rows as covering this would be the paper-ahead-of-evidence failure the matrix
   exists to prevent. At least: text is scanned before egress, a refusal blocks the embedding
   rather than embedding a redacted copy silently, and no vector row crosses a project boundary in
   a search result.
3. **The index is derived, and a permission change invalidates it like a cache.** The retention
   schedule (ADR-0009) already classes embeddings and caches as derived and rebuildable with
   immediate invalidation on permission change. An index that outlives a revoked membership is a
   cross-tenant leak wearing a different shape: the vectors are not the record, but a nearest-
   neighbour hit is a citation, and a citation is disclosure. Isolation must be enforced in the
   query, not by filtering results afterwards.
4. **Full-text search stays.** This is an additional retrieval path, not a replacement, and a
   deployment that configures no provider must behave exactly as v1 does. Same posture as
   telemetry and SMTP: off unless configured, and registering nothing when it is not.
5. **PostgreSQL holds the vectors** (`pgvector`), not a second datastore. The alternative is
   another service to operate, back up, restore, and keep isolated, and this project already
   decided that the database is the source of truth and that operational surface is a cost.
   Backup and restore must carry or rebuild the index; rebuilding is acceptable and re-incurs the
   egress, which is a cost worth stating rather than discovering.

**Left blank on purpose: the provider.** A self-hosted embedding model and a hosted API differ on
exactly the question this ADR is about — whether text leaves the boundary at all. A self-hosted
model makes point 1 much cheaper and adds a model to operate; a hosted API is the reverse. The
project owner names it, and if the answer is a self-hosted model then most of this ADR gets easier
rather than different.

## Consequences

- A question phrased differently from the record that answers it becomes findable. That is the
  whole point and it is genuinely valuable for handover, which is what this system is for.
- A third egress path exists, permanently, and every future feature has to remember it. Two paths
  were already enough to require a stated tagging vocabulary for telemetry.
- Re-embedding on every edit costs money or GPU time per revision, and a knowledge system that
  keeps every revision has a lot of them. A policy on which revisions get embedded is part of this
  decision, not a detail after it.
- Restoring a backup either restores vectors or re-embeds, and re-embedding means the restore drill
  now has an egress in it.

## Alternatives considered

- **Stay with full-text search.** Free, and it is what v1 does. It does not answer the paraphrase
  case, which is the case a later owner actually has. Rejected only if the owner judges the gap
  worth a third egress path; entirely defensible otherwise, and it is the status quo if this ADR
  is not approved.
- **A dedicated vector database.** Better at scale, and a second store to operate, isolate, back
  up, and restore. Rejected against `pgvector` unless the corpus outgrows PostgreSQL, which for a
  per-workspace knowledge base is not the near term.
- **Embed without scanning, and rely on the search path's redaction.** Rejected outright. The scan
  has to happen before the text leaves; redacting what comes back is redacting the wrong copy.
- **A derived index treated as authoritative for search.** Rejected by `info.md` directly: the
  index is never the source of truth.
