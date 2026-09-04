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

Phases 0 to 11 are **complete**, and the v1 gaps named at the end of Phase 11 are closed. Phase 1
delivered `DevBuddy.Domain`; Phase 2 the `UseCaseExecutor` pipeline and the first 41 of what is now
58 operations; Phase 3 PostgreSQL, full-text search, and MinIO; Phase 4 identity, authorization,
and tenant isolation; Phase 5 the lifecycle and audit history; Phase 6 read-only analysis, the real
secret scanner and redactor, the path and URL guards, and source synchronisation from a mounted
working copy; Phase 7 the three hosts — the HTTP API, the MCP server over stdio and authenticated
HTTP, and the console; Phase 8 the provisioning operations and the React administration UI in
`web/admin`; Phase 9 machine tokens and the Claude and Codex plugin packages; Phase 10 the
container images, the Compose stack, backup and restore, and the supply-chain checks; Phase 11 the
personal-data policy and retention enforcement. 433 .NET tests and 31 web tests exist, and all of
them pass in this environment. 32 of 33 controls are `TESTED`; SB-29 is `IMPLEMENTED` and moves to
`TESTED` when a release actually ships one.

**Every operation a person needs is reachable from `web/admin`.** Team administration is the
`Teams` screen, standing up another workspace is the `Workspaces` screen (both gated on the
permission, so a viewer is offered neither), and deleting a project is on the project list —
behind typing the project's name back, because it takes records, their history, and the evidence
bytes with it and there is no undo. Adding somebody to a team picks them from the workspace's own
members rather than asking for an identifier to be typed.

**Source synchronisation can now read the GitHub API, opt-in.** `GitHubOptions.Mode` defaults to
`WorkingCopy` — the mounted-checkout reader Phase 6 shipped, unchanged. Setting it to `GitHubApi`
(a token, and `api.github.com` added to `OutboundAccess:AllowedHosts` — the empty-allow-list
default is not weakened by this existing) swaps in a client that reads pull requests, issues, and
review comments live, and answers `analyze_change_impact` against a commit in a pack file the same
way a checkout does. `sync_sources` reports open pull request and issue counts when the active
client can answer that; a working-copy-backed installation still reports them as absent, honestly,
not as zero.

**SB-18, the personal-data policy, is denied by default on the AI channel and nowhere else.**
Customer, production, and personal data are blocked from a draft and redacted from a read whenever
the caller is on the AI channel and the project's AI access policy carries no approved bounded
scope. A human is never subject to it — SB-18 is an AI data policy, not a general content
restriction — and a secret is still refused even inside an approved scope, per `info.md`.

**SB-27, retention, is enforced for audit events, evidence, backups, and exports.**
`dotnet run -- retention` is a console command, outside the pipeline for the same reason `restore`
is, run on whatever schedule the operator's own cron provides. `export_project` now writes an
actual copy — records, work items, and evidence bytes — instead of only a manifest, so there is
something for the sweep to purge. A deleted project is purged immediately by `delete_project`
itself rather than by a lagging sweep. Application log retention remains a container log-driver
setting outside this codebase, sized in `docker/compose.yaml`, not tested here.

**A project can be deleted.** `delete_project` (Administrator, workspace- or project-scoped) removes
its work items, records with their full revision history, evidence rows and bytes, source
repositories, and project-scoped memberships, immediately. Audit history survives the project it
describes — deleting content is not the same as erasing that the deletion happened.

**A team can be created, renamed, staffed, and deleted.** The entity and its table existed since
Phase 1 with nothing reading or writing them; `create_team`, `rename_team`, `delete_team`,
`list_teams`, `list_team_members`, `add_team_member`, and `remove_team_member` close that. A team
still carries no permission of its own — `Membership` decides what anyone may do, exactly as
before.

**A second workspace can be created — by an existing workspace administrator, not by a new
"installation administrator" role.** `create_workspace` takes a sponsor workspace the caller
already administers, authorises against it through the ordinary pipeline, and makes the caller
administrator of the new one, the same shape `IInstallationBootstrapper` uses for the first one.
`IInstallationBootstrapper` is unchanged: it still refuses once any workspace exists, because it is
still the zero-membership case with nobody to authorise. There is no installation-wide superuser
concept anywhere in this system.

**Setup and recovery tokens are delivered by `IEmailSender`.** `EmailOptions.Provider` defaults to
`Log`: no SMTP configured means the token is logged, the same "an operator completes this by hand"
behaviour this system always had — fixed, not just kept, since the equivalent code before this
port existed logged a warning claiming the token was written to the log without actually including
it. Setting `Provider` to `Smtp` (MailKit) delivers it for real, to the account's own address.

**A release is a `v*` tag, and the workflow signs what it publishes.**
`.github/workflows/release.yml` gates on the full suite, then builds every RID in
`docs/operations/release-matrix.md`, pushes the three images to GHCR, generates one SBOM per host,
and attests all of it with GitHub's keyless OIDC identity — no signing key to hold. It leaves the
release as a draft, because the checklist it cannot run (hand-run smoke tests, the restore drill)
is the half a person has to record. SB-29 stays `IMPLEMENTED` until a tag is actually cut.

**Application log retention is the operator's, and `docs/operations/logging.md` is the runbook.**
Three options with their trade-offs, how to measure the real volume first, and the part that
matters more than the window: with no SMTP configured, `EmailOptions.Provider` stays `Log` and
setup and recovery tokens are written into the API container's log on purpose.

**Restore is a console command, not an operation.** Every operation is authorised against a
membership, and a restore from total loss runs against a database with no memberships in it, so
`restore_system` could never have succeeded and is gone. A test asserts no restore operation
exists. Backup is still an operation, because that one has a caller.

**A backup carries rows and artefacts.** It is logical rather than `pg_dump`, because running an
external program from product code would break the no-execution guard. Sessions are not restored;
passwords and machine tokens are.

**Identity over MCP stdio is a machine token in `DEVBUDDY_TOKEN`.** `DEVBUDDY_ACTOR` is gone: it
let anybody who could start the process start it as anybody, and a test fails if either plugin
package mentions it. A token carries exactly its owner's permissions and is revocable on the next
call.

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

All three hosts read `appsettings.json` in the working directory and environment variables
prefixed `DEVBUDDY_`, and refuse to start without a connection string rather than inventing one.

The web client uses **Bun**, not npm — that is what this machine has:

```bash
cd web/admin && bun install && bun run build && bun test
```

`web/admin/src/api/operations.ts` is generated from `GET /operations` and committed. Never edit it
by hand; regenerate it with the command in `AGENTS.md` after changing any operation or its
request or response record, or the drift test fails.

The stack runs from `docker/compose.yaml`:

```powershell
docker compose -f docker/compose.yaml up -d
```

Images are chiseled: no shell, no package manager. That is why the API's container health check is
`dotnet DevBuddy.Api.dll --health-check` rather than curl, and why a `RUN` in a runtime stage is
impossible.

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
