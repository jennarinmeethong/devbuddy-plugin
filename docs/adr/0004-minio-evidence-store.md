# ADR-0004: MinIO as the default evidence store

- Status: Accepted
- Date: 2026-09-01
- Phase: 3

## Context

`info.md` requires original evidence such as logs, screenshots, and exports to be stored as files
or object storage, with metadata and references retained in PostgreSQL, and requires that data and
evidence persist across container replacement without a mandatory managed SaaS backend. The project
owner confirmed MinIO as the default on 2026-09-01.

## Decision

`IEvidenceStore` has two adapters. **MinIO, S3-compatible, is the default** and ships in the Docker
Compose stack. The filesystem adapter is retained behind the same port for tests and single-host
installations.

Objects are content-addressed by SHA-256. One bucket per workspace, with a project-scoped key
prefix. Server-side encryption enabled. No public bucket and no presigned URL issued to an
anonymous caller: the API streams evidence after an authorization check, so control SB-12 covers
attachments the same way it covers records. PostgreSQL remains the source of truth for object
metadata and every reference.

## Consequences

- The same adapter works against MinIO self-hosted now and any S3-compatible service later, with no
  code change and no managed-SaaS dependency.
- Streaming through the API costs throughput compared with a presigned URL, and buys an
  authorization check on every evidence read. That trade is deliberate.
- The Compose stack gains a service to secure, back up, and keep patched. Covered by SB-30 to SB-33.

## Alternatives considered

- **Filesystem as the default.** Simpler for a single host, but the migration to object storage
  later would touch backup, restore, and retention at once. Rejected by the project owner.
- **Presigned URLs direct to MinIO.** Faster, but moves evidence access outside the authorization
  pipeline. Rejected.
