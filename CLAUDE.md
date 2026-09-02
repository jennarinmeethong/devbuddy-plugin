# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this
repository.

## Read these first

- `AGENTS.md` — structure, commands, style, testing, and security expectations. It is the primary
  contributor guide and applies here in full.
- `info.md` — decisions confirmed by the project owner. Treat as binding. When the owner confirms
  something new, add it there.
- `docs/plan.md` — the phased plan, Phase 0 to Phase 11, each with exit criteria.

## Where the project is

Phases 0 to 5 are **complete**. Phase 1 delivered `DevBuddy.Domain`; Phase 2 the
`UseCaseExecutor` pipeline and 41 use cases; Phase 3 PostgreSQL, full-text search, and MinIO;
Phase 4 identity, authorization, and tenant isolation; Phase 5 the lifecycle end to end with an
audit history that records approver, revision, timestamp, and self-approval. 205 tests pass, 51 of
them security scenarios against real PostgreSQL. Nine of 33 controls are `TESTED`.

**Phase 6 (analysis, sources, and safety scanners) is next.** There is still no GitHub client, no
secret scanner, no MCP server, and no UI.

**The redactor that ships today redacts nothing.** `UnimplementedRedactor` keeps the pipeline
runnable until Phase 6. Never describe secret redaction as working, and do not connect real
project data.

## Commands

```powershell
dotnet build DevBuddy.slnx -c Release
dotnet test DevBuddy.slnx -c Release
dotnet format DevBuddy.slnx --verify-no-changes --severity warn
```

There is no database, container, or web toolchain yet. Do not suggest `docker compose`,
`dotnet ef`, or `npm` commands until the corresponding phase commits their configuration.

## Architecture in one paragraph

One shared core, three thin hosts. `DevBuddy.Domain` holds entities and invariants and references
nothing. `DevBuddy.Application` holds use cases and ports and depends on Domain only.
`DevBuddy.Infrastructure` implements the ports. `Api`, `McpServer`, and `Cli` translate a transport
into a use-case call. Cross-cutting concerns — validation, identity, authorization, redaction,
audit — live in one Application pipeline so no host can skip them. This is what lets the MCP
surface be a deliberate allow-list over existing use cases rather than a second implementation.

## Things that are easy to get wrong here

- **`CR` is banned.** Change Request and Code Review are separate record types. Write
  `change_request` and `code_review` in full, in code, schema, API, and UI.
- **No SQLite.** PostgreSQL in development and production, and in tests via Testcontainers.
- **No embeddings or vector search in v1.** PostgreSQL full-text search and structured filters only.
- **Warnings are errors.** Fix them rather than suppressing them.
- **AI access is denied by default per project.** Nothing reaches the MCP tool surface unless it is
  added to the allow-list on purpose.
- **Never execute repository scripts** — no builds, restores, or tests — while analysing a
  repository under study. Analysis is read-only.
- **`IMPLEMENTED` is not `TESTED`.** In `docs/security/verification-matrix.md`, a row moves to
  `TESTED` only when a passing test exists. Never report a control as done before that.

## Reporting

State what was actually verified. If a build or test was not run, say so. If a phase exit criterion
is unmet, say which one. The whole point of this project is knowledge that a later owner can trust,
and that standard applies to how the work on it is described.
