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

Phases 0 to 7 are **complete**. Phase 1 delivered `DevBuddy.Domain`; Phase 2 the
`UseCaseExecutor` pipeline and 41 use cases; Phase 3 PostgreSQL, full-text search, and MinIO;
Phase 4 identity, authorization, and tenant isolation; Phase 5 the lifecycle and audit history;
Phase 6 read-only analysis, the real secret scanner and redactor, the path and URL guards, and
source synchronisation from a mounted working copy; Phase 7 the three hosts — the HTTP API, the
MCP server over stdio and authenticated HTTP, and the console. 332 tests pass. Twenty-three of 33
controls are `TESTED`.

**Source synchronisation reads a working copy, not the GitHub API.** Pull requests, issues, and
review threads are not available, and `analyze_change_impact` on a commit stored in a pack file
reports that rather than returning an empty answer.

**Phase 8 (web administration UI) is next.** There is no UI and no plugin package.

**Nothing creates a user, a workspace, a project, or a work item except the one-time
bootstrap.** `IInstallationBootstrapper` makes the first workspace and the first administrator on
an empty database, and refuses afterwards. There is no operation for provisioning anything else,
so `grant_membership` can only add somebody who already exists. That belongs with membership
administration in Phase 8; do not describe the current system as able to onboard a second person.

**Account recovery tokens are written to the application log** because no delivery channel exists
yet. It is called out in the code and in the endpoint summary, and it goes away when an email
transport lands.

## Commands

```powershell
dotnet build DevBuddy.slnx -c Release
dotnet test DevBuddy.slnx -c Release
dotnet format DevBuddy.slnx --verify-no-changes --severity warn
```

The console runs migrations and the one-time bootstrap:

```powershell
dotnet run --project src/hosts/DevBuddy.Cli -- migrate
dotnet run --project src/hosts/DevBuddy.Cli -- operations --ai
```

It reads `appsettings.json` in the working directory and environment variables prefixed
`DEVBUDDY_`, and refuses to start without a connection string rather than inventing one.

There is no container or web toolchain yet. Do not suggest `docker compose` or `npm` commands
until the corresponding phase commits their configuration.

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
