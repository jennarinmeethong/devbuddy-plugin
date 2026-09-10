# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this
repository.

## Read these first

- `AGENTS.md` — structure, commands, style, testing, and security expectations. It is the primary
  contributor guide and applies here in full.
- `info.md` — decisions confirmed by the project owner. Treat as binding. When the owner confirms
  something new, add it there.
- `docs/plan.md` — the phased plan, Phase 0 to Phase 12, each with exit criteria. Phase 12 is
  approved as of 2026-09-10: 12A and 12B are complete, 12C is not started and needs ADR-0012 and
  ADR-0013 confirmed in `info.md` before any of it is written.

## Where the project is

**v1 is released, and Phase 12 followed it.** Phases 0 to 11 are complete, the v1 gaps named at the end of Phase 11 are
closed, and `v1.0.0` is published from `9a8ebf0` — signed, an SBOM per image, and the attestations
verified from outside the workflow that built them. Phase 1 delivered `DevBuddy.Domain`; Phase 2
the `UseCaseExecutor` pipeline and the first 41 of what is now 58 operations; Phase 3 PostgreSQL,
full-text search, and MinIO; Phase 4 identity, authorization, and tenant isolation; Phase 5 the
lifecycle and audit history; Phase 6 read-only analysis, the real secret scanner and redactor, the
path and URL guards, and source synchronisation from a mounted working copy; Phase 7 the three
hosts — the HTTP API, the MCP server over stdio and authenticated HTTP, and the console; Phase 8
the provisioning operations and the React administration UI in `web/admin`; Phase 9 machine tokens
and the Claude and Codex plugin packages; Phase 10 the container images, the Compose stack, backup
and restore, and the supply-chain checks; Phase 11 the personal-data policy and retention
enforcement. 539 .NET tests and 36 web tests exist, and all of them pass in this environment —
count them rather than trusting this sentence, which has been stale twice already: it sat at the
release figure of 433 and 31 while both grew, and at 495 and 36 through Phase 12. `docs/plan.md`
keeps the per-phase figures, and the ones under *v1 is released* are what passed at `v1.0.0`; they
are a record and are not updated. All 33 controls are `TESTED`; SB-29, the last one, closed on that
publication.

What v1 did **not** claim was four platforms built but never run, no `linux/arm64` image, and the
operator-side facts about application logs. Phase 12 closed two of those three: the arm64 images
are built and started, and the log facts are decided rather than deferred — the sweep is scheduled
and tokens are no longer written to a log by default. The four unrun platforms remain, now as an
accepted decision rather than a gap. `docs/security/release-readiness.md` is the current statement
of what is accepted and by whom.

**Every operation a person needs is reachable from `web/admin`.** Team administration is the
`Teams` screen, standing up another workspace is the `Workspaces` screen (both gated on the
permission, so a viewer is offered neither), and deleting a project is on the project list —
behind typing the project's name back, because it takes records, their history, and the evidence
bytes with it and there is no undo. Adding somebody to a team picks them from the workspace's own
members rather than asking for an identifier to be typed.

**The API host serves that UI, from inside its own image.** `docker/Dockerfile.api` builds
`web/admin` with Bun in a stage of its own and copies `dist` into `wwwroot`, so the client and the
API it is generated from are one origin, one image, and one thing to deploy — a reverse proxy in
front of the stack now needs `reverse_proxy 127.0.0.1:8080` and nothing else. A static-serving
container was the alternative and was refused: `nginx` or `caddy` would put a shell and a package
manager into a stack whose images are chiseled so that there is nothing in them to execute. The
client therefore calls the API **at the root**, not under a prefix, `vite.config.ts` proxies the
API's own top-level routes in development so the dev server looks like the host that will serve it,
and source maps are off because these files are public. A host with **no `wwwroot` serves the API
alone**, which is what running from source does, and `AdminUiTests` covers that case alongside the
one that matters more: the fallback for the client's routes must never put an HTML page in front of
an endpoint that answered with JSON, a refusal, or a 404.

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
itself rather than by a lagging sweep. Application log retention is enforced here too, by the
same sweep, since the file sink landed.

**The stack schedules its own retention sweep (Phase 12B).** `docker/compose.yaml` runs a
`retention` service on `retention --every 24h` in the console image. A scheduling mode on the
console rather than a `cron` sidecar, because the images are chiseled and a sidecar would put a
shell back into a stack that has nothing in it to execute. It stays **outside** the pipeline for
the reason the sweep was put there: a pass spans every workspace and project and has no caller to
authorise it against, so the loop acquires no actor, no membership and no tenant context. First
pass is immediate, a failed pass is reported and the loop continues, and a scope is created per
pass rather than held for the process. `--every 24` is refused rather than read as twenty-four
days.

**A token is never written to a log unless an operator asked (Phase 12B).**
`Email:AllowTokensInLog` is false, so with `EmailOptions.Provider` on its default of `Log` the
fallback sender records that a message could not be delivered, to whom, and how to fix that, and
writes the token nowhere. v1 wrote them unconditionally and `release-readiness.md` carried that as
an accepted risk. Consequence to know: **inviting somebody still works with no mail server**
(`create_user_account` returns the setup token in its own response), and **self-service recovery
deliberately does not** until SMTP or the opt-in is set. Refusing to start without a delivery
channel was the alternative and was rejected — it breaks a plain `docker run`.

**Application logs: option 2 of `docs/operations/logging.md` is the option in force**, confirmed
2026-09-10. Logs go to Loki through the observability overlay and its ninety days is the retention
of record; `Telemetry:ExportLogs` still defaults to false in code (log export is a third egress
path) but the overlay sets it true, which it could not do while tokens were in those logs.

**`linux/arm64` container images are built and started.** The Dockerfiles cross-compile —
`--platform=$BUILDPLATFORM` on the SDK stage, `-a $TARGETARCH` for the output — so an arm64 image
costs a release minutes rather than the hours emulating a compiler would. Nothing RUNs in a runtime
stage, which is what makes that possible. All three were started under emulation, recorded as
emulated rather than as hardware. The release workflow checks the non-root user **per
architecture**, because a manifest list can hold one image that drops root and one that does not.

**The four unrun RIDs keep shipping.** `osx-arm64`, `osx-x64`, `win-arm64`, `linux-musl-arm64`
stay in the built-but-unverified tier with every release's notes saying they were never started.
Confirmed as a decision, not left as a gap. Nobody may describe them as supported.

**Phase 12C is not started, and its two ADRs are proposals.** ADR-0012 (embeddings and vector
search) and ADR-0013 (the `knowledge-ai-worker`) are written and unapproved. Do not write code for
either until `info.md` confirms them: `info.md` requires separate approval for an embedding
provider and for background processing. The two things those ADRs settle and that any future work
inherits: **embedding text is egress**, so SB-17 and SB-18 apply before text leaves and the
verification matrix gains its own rows; and **a background worker either holds a machine token with
a real membership or touches nothing a person's permissions would gate**, with nothing permitted in
between.

**Telemetry is OpenTelemetry, off unless an endpoint is configured.** `Telemetry:Endpoint` is
empty by default and `AddDevBuddyTelemetry` registers nothing when it is. Configured, it exports
OTLP traces and metrics; `docker/compose.observability.yaml` is an optional overlay carrying a
collector, Prometheus, Loki, Tempo, and Grafana with a provisioned security-controls dashboard.
Log export is a *separate* opt-in that stays false while `EmailOptions.Provider` is `Log`, because
setup and recovery tokens are in those logs by design. The tagging rule — operation name, outcome,
channel, scanner rule name, and nothing else, ever — is stated in `DevBuddyTelemetry` and enforced
by tests; it is why database instrumentation is absent and why URL paths are scrubbed from spans.
**`OpenTelemetry.Instrumentation.AspNetCore` must not go in Infrastructure**: its framework
reference propagates to the console, whose image uses the smaller `runtime` base, and the
container then fails to start at all. The two web hosts add it themselves through the
`configureTracing`/`configureMetrics` callbacks.

**Evidence can now be attached, not only read.** `capture_evidence` and `list_evidence` close a
gap nobody had noticed: `download_evidence` existed, backup and restore carried the bytes,
retention swept them and the isolation tests covered them, but no operation, endpoint or screen
ever called `IEvidenceStore.StoreAsync` — only tests did — so a real installation could never have
had anything to download. Capture is human-only and runs through the pipeline, so a file carrying
a credential is Blocked with **nothing written**, the same shape a draft gets (SB-17): the scan
happens before the store is touched, which is why the request carries an array rather than a
stream. `RecordScanResultAsync` moved onto `IEvidenceStore` — the implementation had existed since
Phase 3 with no way to call it — because stored evidence begins `NotScanned` and the download
refuses to release anything in that state. Capture and download have **streaming routes of their
own** rather than dispatcher entries, because base64 in a JSON envelope inflates a file by a third;
`list_evidence` is ordinary JSON and is dispatched normally. The `Evidence` screen under a project
is where a person does it.

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
is the half a person has to record. `v1.0.0` is published, so SB-29 is `TESTED`; what was re-run
for that tag and what was carried over is recorded in `docs/operations/release-matrix.md`.

**Application log retention is enforced and tested.** `Logging:File:Path` (set by
`docker/compose.yaml`) turns on a Serilog daily file, and `dotnet run -- retention` deletes files
past `Logging:File:RetentionDays` — the same sweep that handles audit events, evidence, backups,
and exports, and the last of the ten schedule rows to leave "operator responsibility". Off unless
the path is set, because the containers run read-only and a default that wrote files would break
every plain `docker run`. **`Serilog.AspNetCore` must not be used**: same framework-reference trap
as the OpenTelemetry ASP.NET instrumentation, and `Serilog.Extensions.Hosting` is what
Infrastructure takes instead. `docs/operations/logging.md` has the three options and what each
costs, plus the part that matters more than the window: with no SMTP configured,
`EmailOptions.Provider` stays `Log` and setup and recovery tokens are written into the log on
purpose.

**Restore is a console command, not an operation.** Every operation is authorised against a
membership, and a restore from total loss runs against a database with no memberships in it, so
`restore_system` could never have succeeded and is gone. A test asserts no restore operation
exists. Backup is still an operation, because that one has a caller.

**A backup carries rows and artefacts.** It is logical rather than `pg_dump`, because running an
external program from product code would break the no-execution guard. Sessions are not restored;
passwords and machine tokens are.

**Identity over MCP stdio is a machine token in `DEVBUDDY_TOKEN`, bound to one user and one
workspace.** `DEVBUDDY_ACTOR` is gone: it let anybody who could start the process start it as
anybody, and a test fails if either plugin package mentions it. A token carries exactly its
owner's permissions **in the one workspace it was minted in**, and is revocable on the next call.
`AuthorizationService` refuses a request naming another workspace before it looks the caller up,
so the check costs no query and cannot be reached past. A signed-in person's HTTP session carries
no such ceiling and is unaffected. Tokens issued before this scoping carry no workspace and are
**refused rather than adopted** — the row says who owns it and nothing about where it was meant to
work — and are listed as needing replacement so their owners can mint a successor. Neither plugin
package holds a literal setting any more: both name variables and take them from the environment
the session was launched in, which matters most for Codex, whose file is account-wide.

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
