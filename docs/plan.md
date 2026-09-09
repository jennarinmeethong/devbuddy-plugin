# DevBuddy Knowledge System — Implementation Plan

## Context

`info.md` records the decisions the project owner has confirmed for this project: an MCP-first
engineering-knowledge system built on a single shared .NET core, exposed through a console app,
a web API, and an MCP server, with separate (thin) Claude and Codex plugin packages, a React
admin UI, PostgreSQL as the source of truth, self-hosted Docker deployment, and a strict
AI-data policy plus security baseline.

Today the repository is empty scaffolding: `README.md` (title only), `AGENTS.md`, `CLAUDE.md`,
and `info.md`. There is no solution, source tree, tooling, or test harness. `info.md` is a
decision log, not an executable plan — it states *what* must be true, not *in what order* it
gets built or *how each piece is proven done*.

This plan converts `info.md` into a phased, buildable roadmap. Every phase names its
deliverables, the files/projects it creates, and explicit exit criteria, so progress is
verifiable rather than asserted.

**Status:** approved by the project owner on 2026-09-01.

**Gating note (from `info.md`):** implementation must not begin until the project owner
explicitly instructs to start. Approval of this plan is that instruction for Phase 0 onward.
Each later phase still starts only after the previous phase's exit criteria are met. No real
project data, credentials, or source repositories are connected until Phase 11 verification
passes.

---

## Guiding constraints (carried from `info.md`)

| Constraint | Consequence for the build |
|---|---|
| Clean start | No code, schema, or naming carried over from any previous DevBuddy project. |
| MCP-first, one core | Domain/Application logic lives in one place; console, API, and MCP are thin entry points. |
| Claude and Codex separately | Two plugin packages, two instruction sets — **one** shared core and **one** MCP tool surface. No per-AI logic in the core. |
| `change_request` ≠ `code_review` | Two distinct record types. The abbreviation `CR` is banned from code, schema, docs, and UI. |
| PostgreSQL only in v1 | No SQLite application database, including for dev. Tests use a real PostgreSQL via Testcontainers. |
| No embeddings in v1 | Full-text search + structured filters only. Any future vector index is a derived index, never a source of truth. |
| AI denied by default | Per-project opt-in; secrets never leave the boundary; cross-project access blocked server-side. |
| Human-gated lifecycle | AI may draft; approve/publish/correct/archive/sync/redact/backup stay under human or internal-system control. |
| Self-hosted | Self-contained .NET publish + Docker; data and evidence survive container replacement. |

---

## Target repository layout

```
devbuddy-plugin/
  DevBuddy.slnx                    # .NET 10 XML solution format
  global.json                      # SDK pin, rollForward latestFeature
  Directory.Build.props            # net10.0, nullable, warnings-as-errors, analyzers
  Directory.Packages.props         # central package management
  .editorconfig / .gitattributes   # style rules; LF normalised on every platform
  docs/
    plan.md                        # this plan, committed (Phase 0)
    adr/                           # architecture decision records
    security/
      threat-model.md
      security-baseline.md
      verification-matrix.md
    operations/
      deployment.md
      release-matrix.md
  src/
    core/
      DevBuddy.Domain/             # entities, enums, invariants — zero external deps
      DevBuddy.Application/        # use cases, ports (interfaces), policies, DTOs
      DevBuddy.Infrastructure/     # EF Core/Npgsql, evidence store, Git/GitHub, scanners
    hosts/
      DevBuddy.Api/                # ASP.NET Core minimal API + auth + admin endpoints
      DevBuddy.McpServer/          # MCP server, safe scoped tool surface only
      DevBuddy.Cli/                # console app (admin + local operations)
  web/
    admin/                         # Vite + React + TypeScript + Tailwind + shadcn/ui
  plugins/
    claude/                        # .claude-plugin/plugin.json, commands, skills, mcp config
    codex/                         # AGENTS.md + config.toml MCP server entry
  docker/
    Dockerfile.api / Dockerfile.mcp / Dockerfile.cli
    compose.yaml                   # api, mcp, postgres, object storage
  tests/
    DevBuddy.Domain.Tests/
    DevBuddy.Application.Tests/
    DevBuddy.Infrastructure.Tests/ # Testcontainers PostgreSQL
    DevBuddy.Api.Tests/            # WebApplicationFactory integration
    DevBuddy.McpServer.Tests/
    DevBuddy.Security.Tests/       # isolation, injection, secret-leak, approval-binding
```

**Dependency rule (enforced, not just documented):** `Domain` → nothing; `Application` → `Domain`;
`Infrastructure` → `Application` + `Domain`; hosts → `Application` (+ `Infrastructure` for DI
composition only). Enforced by an architecture test in `DevBuddy.Application.Tests`.

---

## Technology choices

These are the concrete picks that satisfy `info.md`; each gets an ADR in `docs/adr/`.

- **Runtime:** .NET 10 (`net10.0`), C# 14, nullable enabled, `TreatWarningsAsErrors`.
- **Persistence:** EF Core 10 + Npgsql. Migrations in `Infrastructure`. `tsvector` + GIN index for FTS.
- **API:** ASP.NET Core minimal APIs, OpenAPI document generated at build.
- **Auth:** ASP.NET Core Identity (own account system, per `info.md`) + short-lived JWT access
  tokens + rotating refresh tokens, lockout, and a token-based recovery flow. No SSO/IdP in v1.
- **MCP:** official `ModelContextProtocol` C# SDK. **Both transports ship in v1** — stdio for the
  local Claude/Codex plugins, and authenticated HTTP for self-hosted/remote use, sharing the API's
  auth and the same tool allow-list.
- **Evidence storage:** `IEvidenceStore` port. **MinIO (S3-compatible) is the default** and ships
  in `compose.yaml`; the filesystem adapter is kept for tests and single-host installs only.
  Content-addressed by SHA-256.
- **Frontend:** Vite + React + TypeScript + Tailwind + shadcn/ui + TanStack Query + React Router.
- **Testing:** xUnit + Testcontainers (PostgreSQL) + `WebApplicationFactory`.
- **Observability:** OpenTelemetry traces/metrics, exported over OTLP and off unless an endpoint
  is configured; `docker/compose.observability.yaml` is an optional overlay with a collector,
  Prometheus, Loki, Tempo, and Grafana. Serilog was not taken up: the hosts log to standard output
  through the default provider and the container's log driver owns the file handling, which is
  what a chiseled image with no writable root wants. Audit remains a separate, append-only store —
  not application logs, and not telemetry. See `docs/operations/observability.md` for the tagging
  rule that keeps span and metric tags to a fixed vocabulary.
- **CI:** GitHub Actions — build, test, architecture tests, security tests, container build, SBOM.

---

## Phase 0 — Foundations and governance

**Goal:** a repository that can hold the work, with the decision record and security documents
in place before any behaviour is written.

1. Commit `docs/plan.md` (this plan, rendered as project documentation) alongside `info.md`.
2. Create `DevBuddy.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`,
   `.editorconfig`, `.gitattributes`, `.gitignore`, and empty project shells for every project in
   the layout above.
3. Write `docs/security/security-baseline.md` and `docs/security/threat-model.md` from the
   `info.md` security sections, in the required per-risk shape: **asset · trust boundary ·
   control · verification method · owner · residual risk**.
4. Write `docs/security/verification-matrix.md` listing every control and the test that will
   prove it — initially all rows are `NOT IMPLEMENTED`. This file is the single source of truth
   for "is this actually secure yet", and is updated only when a test exists and passes.
5. Seed `docs/adr/` with ADR-0001..0010 for the technology choices above and the five decisions
   confirmed on 2026-09-01.
6. GitHub Actions workflow: restore, build, test, format check.
7. Update `AGENTS.md` and `CLAUDE.md` with the now-real build/test commands.

**Exit criteria:** `dotnet build` and `dotnet test` succeed on an empty-but-wired solution; CI green;
security docs exist with every row explicitly marked unimplemented.

**Status: COMPLETE (2026-09-01).** 12 projects build with 0 warnings, 6 scaffold tests pass,
`dotnet format --verify-no-changes` exits clean, 33 controls catalogued and every one recorded
`NOT IMPLEMENTED`, 10 ADRs written. The CI workflow is written and verified locally command by
command; it has not yet run on GitHub, so "CI green" is not yet demonstrated.

---

## Phase 1 — Domain model

**Goal:** the vocabulary of the system, with invariants, and no infrastructure.

**Project:** `src/core/DevBuddy.Domain`

Aggregates and entities:

- **Tenancy:** `Workspace`, `Team`, `Project`, `Repository`, `Environment`. Every downstream
  entity carries `WorkspaceId` + `ProjectId`.
- **Work identity:** `WorkItem` — identifier, `WorkItemType`, title, goal, scope, exclusions,
  related repositories/modules, stakeholders.
  `enum WorkItemType { Develop, Enhance, FixBug, ChangeRequest, CodeReview }` — spelled out; an
  analyzer/test forbids the token `CR` as a type name, column name, or identifier.
- **Knowledge:** `KnowledgeRecord` aggregate with `RecordKind`
  `{ ContextReference, DeliveryState, Decision, TechnicalKnowledge, ChangeImpact, Handover, CodeReviewFeedback }`,
  each mapping to one bullet in the `info.md` "Knowledge Records" section:
  - `Decision` — decision, rationale, alternatives considered, approver, decision date.
  - `DeliveryState` — status, owner, last-updated, acceptance criteria, verification method, evidence.
  - `ChangeImpact` — affected files/components, migration/rollback, limitations, risks, bugs,
    root causes, workarounds.
  - `Handover` — remaining work, open questions, cautions, next steps.
  - `CodeReviewFeedback` — feedback, resolution status, rationale when not applied.
- **Revisioning:** `RecordRevision` (immutable; `ContentHash`, structured fields + Markdown body
  with front matter), `RecordStatus { Draft, PendingApproval, Approved, Published, Archived }`,
  `Approval` (approver, **approved revision hash**, timestamp, `ApproverWasDraftCreator` flag).
- **Provenance:** required value object on every record — source, author, timestamp, evidence links.
- **Evidence:** `EvidenceObject` — content hash, media type, size, storage key, redaction state.
- **Access:** `User`, `Membership`, `Role { Viewer, Contributor, Reviewer, Administrator }`,
  `ProjectAiAccessPolicy` (default: **deny**).
- **Audit:** `AuditEvent` — actor, action, resource, scope, timestamp, outcome. No payload copying.

Invariants enforced in the domain (not in handlers): a record cannot be published without an
approval bound to its current revision hash; a revision is never mutated; provenance is not
optional; every entity is workspace/project scoped.

**Exit criteria:** `DevBuddy.Domain.Tests` covers each invariant, including
"publish with an approval for an older revision is rejected"; architecture test proves `Domain`
has no project or third-party references.

**Status: COMPLETE (2026-09-01).** 45 domain tests and 9 architecture/naming tests pass; solution
builds with 0 warnings and `dotnet format --verify-no-changes` is clean. Both guard tests were
mutation-checked: introducing a `CRSummary` type and a package reference on `Domain` made them
fail, and removing the mutations made them pass again. SB-20, SB-23, SB-24, SB-25 and SB-26 moved
to `IMPLEMENTED` in the verification matrix; none is `TESTED`, because each still needs a
persistence or end-to-end half from a later phase.

Two naming choices differ from the sketch above, to avoid collisions that would bite later:
`Repository` is `SourceRepository` (the persistence pattern shares the word) and `Environment` is
`DeploymentEnvironment` (`System.Environment` shares the word).

---

## Phase 2 — Application layer: use cases and ports

**Goal:** every operation in `info.md` expressed as a use case behind an interface, with
authorization as a first-class step — independent of transport, persistence, and AI provider.

**Project:** `src/core/DevBuddy.Application`

Ports (interfaces implemented later in `Infrastructure`):
`IKnowledgeRepository`, `IEvidenceStore`, `ISourceSystemClient` (Git/GitHub), `ICodeAnalyzer`,
`ISecretScanner`, `IRedactor`, `IAuthorizationService`, `IAuditSink`, `IClock`, `ISearchIndex`.

Use cases, grouped by the exposure boundary they will later sit behind:

| Group | Use cases | Later exposed to AI? |
|---|---|---|
| Read/search | `SearchKnowledge`, `GetRecord`, `GetWorkItem`, `ListProjects`, `ViewRecordHistory`, `CompareSnapshots` | Yes |
| Analysis (read-only) | `AnalyzeProject`, `AnalyzeCode`, `AnalyzeDocuments`, `AnalyzeArchitecture`, `AnalyzeGitHistory`, `AnalyzeWorkItems`, `AnalyzeTestEvidence`, `AnalyzeChangeImpact` | Yes |
| Handover | `GenerateHandover`, `FindOpenQuestions`, `FindMissingEvidence` | Yes |
| Drafting | `CreateDraft` | Yes |
| Lifecycle | `ApproveRecord`, `RequestCorrection`, `PublishRecord`, `ArchiveRecord` | **No** |
| Sources & quality | `SyncSources`, `ValidateProvenance`, `DetectDuplicates`, `DetectStaleness`, `Reindex` | **No** |
| Safety | `DetectSecrets`, `RedactSensitiveData` | **No** (runs in-pipeline) |
| Administration | `Export`, `Backup`, `Restore`, `HealthCheck`, `ManageAccess`, `AuditAccess` | **No** |

Cross-cutting pipeline behaviours applied to every use case, in order:
**validate input → resolve identity → authorize (workspace + project + resource) → execute →
scan/redact outbound content → audit**.

Self-approval rule from `info.md`: `ApproveRecord` permits the draft creator to approve, subject
to the same permission check as any reviewer, and records `ApproverWasDraftCreator = true`.

**Exit criteria:** every use case has unit tests with a fake port set; an authorization test
asserts that *no* use case can execute without an authorization decision (enforced by pipeline
test, not by inspection).

**Status: COMPLETE (2026-09-01).** 40 use cases, 62 application tests, all passing. The
enforcement is structural, not conventional: `HandleAsync` on `UseCase<TRequest, TResponse>` is
`protected internal`, so the API, MCP, and console hosts physically cannot call a use case without
going through `UseCaseExecutor`. `AuthorizationEnforcementTests` then drives all 40 through the
pipeline with a denying authorization service and asserts both a Denied result and **zero port
interactions**, so a use case that read the database before denying would fail. A registry-coverage
test makes adding a use case without adding it to that suite a build failure; it was
mutation-checked by removing one entry and watching it fail.

Five decisions worth knowing, all visible in the code:

- **Four ports the plan sketch did not name** were needed: `IProjectDirectory`, `IAccessDirectory`,
  `IAuditReader`, and `IAdministrativeOperations`. Listing projects, managing membership, reading
  audit, and running backups all had to read from somewhere, and folding them into
  `IKnowledgeRepository` would have blurred four responsibilities into one.
- **`AuditEvent` was amended** to take a nullable workspace and project rather than a
  `ProjectScope`. Real auditable actions happen above a project: listing projects, granting
  membership, taking a backup.
- **`Permission` is `PermissionKind`**, because `Permission` is a reserved type-name suffix. The
  descriptor property is still called `Permission`.
- **Use cases are grouped by family**, one file per family rather than one per class. Forty files
  of forty lines each would have been worse to navigate than eight coherent modules.
- **Eighteen operations are AI-exposed**, pinned by name in a test: exactly search, get, the eight
  analyse tools, find/compare, create a draft, and generate a handover. The other twenty-two are
  Denied and refused before authorization even runs.

---

## Phase 3 — Persistence, search, and evidence

**Goal:** PostgreSQL as source of truth; Markdown bodies and evidence linked to their records.

**Project:** `src/core/DevBuddy.Infrastructure`

- EF Core `DbContext` + configurations + initial migration. Composite indexes lead with
  `(WorkspaceId, ProjectId)` so isolation is also the fast path.
- **Global query filters** on workspace/project derived from the authenticated principal — a
  defence-in-depth layer *behind* explicit authorization, never instead of it.
- Markdown body stored with structured front matter, linked one-to-one to its `RecordRevision`.
- Full-text search: generated `tsvector` column + GIN index, combined with structured filters
  (project, work item type, record kind, status, date range, owner). No embeddings, no vector column.
- `IEvidenceStore` adapters: **S3-compatible (MinIO) is the default**, filesystem is the fallback
  for tests and single-host installs. Content-addressed by SHA-256; bucket per workspace with a
  project-scoped key prefix; server-side encryption enabled; **no public or presigned-to-anonymous
  access** — the API streams evidence after an authorization check. Object metadata and all
  references stay in PostgreSQL, which remains the source of truth.
- Source-system snapshots: retained links + snapshot metadata for Git refs, commits, PRs, and
  issues so knowledge can be re-verified against its origin.

**Exit criteria:** `DevBuddy.Infrastructure.Tests` runs against real PostgreSQL via Testcontainers;
migration applies to an empty database and round-trips every aggregate; a search test proves
that a record in project B never appears in a project-A query.

**Status: COMPLETE (2026-09-01).** 25 integration tests pass against a real PostgreSQL 17 and a
real MinIO, both started by Testcontainers. All three exit criteria are met: the migration applies
to an empty database and creates all 16 tables; every aggregate round-trips including jsonb front
matter, provenance, approvals, and correction reasons; and
`a_record_in_another_project_never_appears_in_a_search` puts identical text in two projects and
proves each query sees only its own.

Four things worth knowing:

- **Persistence maps explicit row types rather than the aggregates** (ADR-0011). The alternative
  needed a private parameterless constructor on thirteen domain types, each weakening the
  invariant that type exists to hold. The one seam is `KnowledgeRecord.Rehydrate`, which re-checks
  every structural invariant on load; a test corrupts a row to claim a published revision that
  does not exist, and the load throws.
- **The global query filters fail closed.** With no workspace in context they match nothing rather
  than everything, so a query somebody adds later and forgets to scope returns empty. Tested.
- **Immutability is enforced at the storage layer too.** `UpdateRecordAsync` refuses a revision
  number that already exists with different content, so a bug in a use case cannot rewrite
  history. That is the persistence half of SB-24.
- **MinIO is what gets tested**, not only the filesystem fallback, because MinIO is what ships.

---

## Phase 4 — Identity, access, and tenant isolation

**Goal:** the product's own login, multi-workspace scoping, and server-side authorization on
every request.

- ASP.NET Core Identity with the product's own accounts; no SSO/IdP.
- Password storage per Identity defaults, lockout on repeated failures, rate limiting on auth
  endpoints, short-lived access tokens + rotating refresh tokens with revocation, and a
  time-boxed single-use account-recovery flow.
- Roles: `Viewer`, `Contributor`, `Reviewer`, `Administrator`, assigned per workspace/project.
- **Caller-supplied project identifiers are never trusted.** Membership and resource permission
  are re-checked server-side on every request; isolation checks cover search results, citations,
  attachments, caches, and exports — not just single-record reads.
- A single-workspace "simple mode" for local use that keeps the multi-project boundaries intact.

**Exit criteria:** `DevBuddy.Security.Tests` includes cross-user, cross-team, cross-project, and
revoked-permission cases across read, search, export, and attachment paths; the corresponding
rows in `verification-matrix.md` flip to `IMPLEMENTED + TESTED`.

**Status: COMPLETE (2026-09-01).** 41 security tests run the real pipeline over the real
infrastructure against real PostgreSQL, plus 29 infrastructure and 69 application tests. All four
paths are covered — record read, search, export, attachment download — with cross-user,
cross-team, cross-project, revoked-grant, and disabled-account cases. SB-11, SB-12, SB-14 and
SB-15 reach `TESTED`, and **Phase 11 scenarios 1 and 2 are exercised**.

Five things worth knowing:

- **The tests found a real bug.** Revoked and expired refresh tokens still worked, because
  `ExecuteUpdate` bypasses the change tracker and a later read in the same scope returned the
  stale tracked entity. The fix reads outside the tracker and stakes the single-use claim with a
  conditional update, which also makes rotation safe against two concurrent refreshes.
- **`download_evidence` was added** as a 41st use case. The exit criteria name an attachment path,
  and there was none: evidence could be listed but not fetched. It is denied to AI, because a raw
  log is not search, get, analyse, draft, or handover.
- **`list_projects` narrows itself on the AI channel.** It is the one AI-exposed operation that
  spans projects, so the per-project policy cannot be applied by a single authorization check.
  Naming a project is itself a disclosure.
- **The shipped redactor redacts nothing.** `UnimplementedRedactor` exists so the pipeline runs
  end to end, and is named so nobody mistakes it for a control. The verification matrix says so
  in SB-17. Phase 6 replaces it, and real project data waits for that.
- **Rate limiting is not here.** Lockout is, and it is the part that does not depend on a
  transport. HTTP rate limiting arrives with the API in Phase 7, and SB-13 says so.

---

## Phase 5 — Knowledge lifecycle and audit

**Goal:** the controlled draft → published path, with approval bound to exact content.

- `create_draft` → `request_correction` → `approve_record` → publish → archive, with drafts
  stored separately from published records.
- Approval binds to the reviewed revision hash. Publishing with a stale approval is rejected at
  the domain level (Phase 1 invariant) and covered by an integration test here.
- Audit history records approver, exact approved revision, timestamp, and whether the approver
  was also the draft creator.
- `view_record_history`, `compare_snapshots`, `audit_access` over the audit store.
- Audit entries record *that* access happened, not the sensitive payload accessed.

**Exit criteria:** an end-to-end integration test drives a record from draft to publish, then
attempts a stale-approval publish and a cross-project approval, and both are rejected and audited.

**Status: COMPLETE (2026-09-01).** `LifecycleAndAuditTests` runs the whole path against real
PostgreSQL — draft, submit, correction with its reason, revision, approval, publish — and then
both failure modes: publishing after the text changed, and approving a record in another project.
Both are rejected and both are audited. SB-19, SB-20 and SB-23 reach `TESTED`, taking the total to
nine, and **Phase 11 scenario 5 is exercised**.

Three things worth knowing:

- **Audit entries gained structured metadata.** info.md requires the audit *history* to record the
  approver, the exact approved revision, the timestamp, and whether the approver was also the
  draft creator. Those lived only on the record. A response can now contribute metadata to its own
  audit entry through `IAuditableResult`, because only the use case knows which revision an
  approval covered.
- **The metadata is capped rather than trusted.** Values are limited to 200 characters and refused
  rather than truncated: a truncated secret is still a leak, and a refusal is visible. The test
  that proves it scans every audit row and detail column for a marker present in the record body,
  and it was mutation-checked by putting the marker into an audit detail and watching it fail.
- **A rejection is audited with its reason.** A refused publication is exactly the event an
  investigation into a stale approval would look for, so it is recorded rather than only returned.

`compare_snapshots` is in this phase in the plan but cannot be exercised yet: `ISourceSystemClient`
has no implementation until Phase 6. The use case and its authorization are covered; the source
system it talks to is not.

---

## Phase 6 — Analysis, sources, and safety scanners

**Goal:** the read-only analysis capability that must exist *before* a record is created, plus
the quality and safety gates.

- Read-only analyzers over projects, code, documents, architecture, Git history, work items, and
  test evidence. **No builds, restores, tests, or repository scripts are ever executed.** Any
  future execution requirement is a separate, explicitly authorized, sandboxed feature.
- `AnalyzeChangeImpact`: given a commit or diff, identify affected modules, APIs, tests, and documents.
- **Work-item source of truth: DevBuddy.** `WorkItem` and every knowledge record derived from it
  are authored and owned here. GitHub issues, pull requests, and commits are imported **read-only**
  as linked references plus snapshot metadata, and are **never written back** in v1.
- `SyncSources`: therefore one-way GitHub import for authorized repositories only — fetch, snapshot,
  refresh, and flag drift between a snapshot and its origin. No bidirectional sync and no
  conflict-resolution engine in scope.
- `ValidateProvenance`, `DetectDuplicates`, `DetectStaleness` for knowledge quality.
- `DetectSecrets` + `RedactSensitiveData` run **before** logs, configuration, or other potentially
  sensitive material is retained *and* before any tool response leaves the boundary. Rule set:
  known credential patterns, connection strings, private keys, plus entropy heuristics.
- All source files, documents, imported records, and tool outputs are treated as **untrusted
  data**. Tool permissions and allowed network destinations are enforced in code and
  configuration — never by prompt text.
- Input validation on every tool: filesystem paths constrained to the authorized project root,
  URLs constrained to an allow-list (blocking internal/link-local addresses), parameterized SQL,
  no shell construction from input.

**Exit criteria:** a corpus test proves secrets are caught and redacted before retention and
before egress; a prompt-injection corpus (malicious content in source files, docs, and PR bodies)
produces no tool call outside the allowed set; path-traversal and SSRF attempts are rejected.

**Status: COMPLETE (2026-09-01).** All four exit criteria are met and eight controls reach
`TESTED` — SB-01, SB-02, SB-03, SB-04, SB-05, SB-06, SB-17 and SB-22 — taking the total to
seventeen of 33. 305 tests pass.

Source synchronisation was missing when the phase was first reported and has since been closed;
what it does and does not cover is at the end of this section.

What landed:

- **A real scanner and redactor sharing one rule set.** One list, used by both, so nothing can be
  reported as sensitive and released anyway. Eight secret shapes plus negatives that must survive
  untouched, because a control that mangles ordinary prose gets routed around within a week.
- **The pipeline now refuses inbound content carrying a credential** rather than storing it.
  Outbound text is redacted; inbound is refused. Silently storing something other than what the
  author wrote, without telling them, is worse than saying no.
- **Analysis that reads and never runs.** Proved twice: a fixture whose build script, Makefile,
  npm hook and MSBuild target would each leave a marker file, and a source scan that fails the
  build if any process API appears in product code.
- **Path and URL guards**, with corpora covering symlink escape, a sibling directory sharing the
  root prefix, the cloud metadata address, IPv4-mapped loopback, and a host resolving to both a
  public and a private address.
- **The three knowledge-quality sweeps**, reporting and never repairing.

Source synchronisation, closed after the phase was first reported:

- **`WorkingCopySourceSystemClient` reads git as files.** HEAD, loose and packed references,
  commit objects, and tree objects, through a zlib stream. Not by running git: "it is only git" is
  exactly the exception that makes SB-04 stop meaning anything.
- **Tested against real git objects.** The fixture writes them — blobs, trees, commits, named by
  the SHA-1 of their own bytes — so the reader is exercised against the format it claims to read
  rather than against a mock of it. Shelling out to git for the fixture would have made the test
  depend on whichever git is on the machine, next to a control forbidding exactly that.
- **Packed objects are not supported, by name.** Reading a pack file means implementing delta
  chains, and a partial implementation fails in ways that look like missing history rather than a
  missing feature. `analyze_change_impact` on a packed commit says so and says what to do about
  it. Snapshots and comparison work either way, because references are readable from `packed-refs`.
- **A provider API adapter is still worth having** for pull requests, issues, and review threads,
  which a working copy does not carry. ADR-0010 is unchanged and the port is unchanged.

---

## Phase 7 — Entry points: API, MCP server, console

**Goal:** three thin hosts over one core.

**`DevBuddy.Api`** — full surface: auth, workspace/team/project/membership administration,
project AI-access policy, draft review/edit/approval, published-record history, role-appropriate
audit visibility, system health, export/backup/restore. OpenAPI document published for the web UI.

**`DevBuddy.McpServer`** — **only** the safe, scoped operations from the Phase 2 table:
`search_knowledge`, `get_record`, `get_work_item`, `list_projects`, the read-only `analyze_*`
tools, `analyze_change_impact`, `find_open_questions`, `find_missing_evidence`,
`view_record_history`, `compare_snapshots`, `create_draft`, `generate_handover`.
Everything else is *absent from the tool list*, not merely refused. A test asserts the exported
tool list equals the allow-list exactly, so a new use case cannot leak into the AI surface by
accident. AI-access policy is evaluated per project on every call: deny by default, opt-in by the
project owner, results further narrowed to the requesting user's permissions, and customer/
production/personal data denied unless a separately approved bounded scope exists.

Two transports, one allow-list: **stdio** for locally launched Claude/Codex plugins (identity from
a machine-scoped token in the plugin config), and **authenticated HTTP** for self-hosted/remote use
(same bearer tokens as the API, same authorization pipeline). The tool-surface test runs against
both so a transport cannot widen the surface.

**`DevBuddy.Cli`** — local and administrative operations: migrate, seed, health, export,
backup/restore, sync, reindex, and manual record inspection. Publishable self-contained.

**Exit criteria:** MCP tool-surface test passes; an integration test proves a project with AI
access disabled returns no content through MCP while the same query through the API (as an
authorized human) succeeds.

**Status: COMPLETE (2026-09-02).** Both exit criteria are met. Six controls reach `TESTED` —
SB-07, SB-08, SB-09, SB-10, SB-13 and SB-16 — taking the total to 23 of 33, and Phase 11
scenario 3 is now fully exercised. 332 tests pass, 0 warnings, formatting clean.

What landed:

- **One dispatcher, three hosts.** `OperationDispatcher` maps an operation name and a JSON body
  onto a use case and runs it through the same pipeline. The API has one route for every
  operation rather than forty hand-written ones, and that is a security property before it is a
  convenience: forty endpoints are forty chances for one of them to disagree with the pipeline
  about identity, permission, redaction, or audit.
- **The MCP tool surface, derived and pinned.** The list is built from `UseCaseCatalog.AiExposed`
  at run time, so an operation that is not marked exposed is *absent* rather than present and
  refused. `ToolSurfaceTests` asserts equality against a list written out independently, and a
  source scan fails the build if anything outside `ToolSurface.cs` constructs a tool — which is
  what stops a transport growing a list of its own.
- **Two transports, one surface.** `--stdio` for a locally launched plugin, authenticated HTTP
  otherwise, sharing the API bearer tokens. The channel is decided by which host is running,
  never by anything in a request.
- **The cross-surface proof.** One record, read over real HTTP by an authorised person and
  refused on the AI channel, then enabled and read. The third step is what makes the second
  evidence rather than a broken path, and a two-caller variant shows AI access is a project
  opt-in and never a bypass of the permissions of the person asking.
- **The console.** `migrate`, `bootstrap`, `operations`, `run`, and shortcuts for health, record
  inspection, export, backup, restore, sync and reindex. The shortcuts build the JSON body and
  take the same dispatch path; nothing in the console reaches past the pipeline.
- **A bootstrap that runs once.** The first workspace and the first administrator cannot come
  through the pipeline, because the pipeline authorises against a membership and there is none
  yet. `IInstallationBootstrapper` is that path, named rather than hidden, and it refuses once a
  workspace exists.
- **A wire-contract guard.** `create_draft` returned 500 because `Provenance` took its evidence as
  `IEnumerable` and exposed it as `IReadOnlyList`: correct for every caller, impossible for a
  serialiser to construct. `WireContractTests` now asks the serialiser about every operation
  argument and result type, so the next one is caught before anything runs.

Known gaps, carried forward rather than papered over:

- **No operation creates a user account.** `grant_membership` can add somebody who exists to a
  workspace, and nothing can bring an account into being except the one-time bootstrap. The
  integration tests seed accounts through the database because an operator would have to. This
  belongs with membership administration in Phase 8.
- **No operation creates a workspace, project, or work item** either, for the same reason. Phase 8.
- **Rate limiting covers the credential endpoints only**, and there is no concurrent-job cap.
  Both are Phase 10 (SB-21).
- **Draft invisibility is proved at the pipeline, not per surface.** SB-26 stays `IMPLEMENTED`
  until there is a test for it through the API and MCP specifically, and a UI to test at all.
- **Account recovery tokens are written to the log**, because no delivery channel exists. Called
  out in the code and in the response, and removed when an email transport lands.

---

## Phase 8 — Web administration UI

**Goal:** the first-release admin experience in `web/admin`.

Scope, exactly as `info.md` defines it: login, account recovery, workspace/team/project and
membership administration, project AI-access policy, draft review/edit/approval,
published-record history, role-appropriate audit visibility, and basic system health.

**Explicitly out of scope:** a web chat. AI questions stay in Claude and Codex.

- Typed client generated from the API's OpenAPI document, so contract drift is a build failure.
- The approval screen displays the exact revision being approved and its hash, and submits that
  hash — the UI cannot approve "the latest" implicitly.
- Role-driven navigation; the UI hides what the server would refuse, and the server refuses regardless.

**Exit criteria:** each listed capability is reachable and exercised in a smoke test; a viewer-role
session cannot see approval or audit surfaces, verified server-side.

**Status: COMPLETE (2026-09-03).** Both exit criteria are met. SB-26 reaches `TESTED`, taking the
total to 24 of 33 with only SB-21 left `IMPLEMENTED` but unproven. 343 .NET tests and 19 web tests
pass, 0 warnings, formatting clean, and the whole stack was driven end to end in a browser against
a real PostgreSQL.

The server half had to come first, because the server could not support an administration UI:
nothing in the system created a user, a workspace, a project, or a work item except the one-time
bootstrap, so an installation had exactly one person in it and no way to gain a second.

What landed on the server:

- **Six provisioning operations** — `create_project`, `create_work_item`, `create_user_account`,
  `list_memberships`, `list_work_items`, `list_records` — all denied to AI. The exported tool
  surface is still the same eighteen names, which is the allow-list doing its job while the
  catalogue grew by six.
- **Accounts with no credential.** `create_user_account` returns a single-use setup token its owner
  redeems to choose a password. An administrator who picked one would know it, and the point of an
  account is that only its owner does. There is no mail transport, so the token is handed to the
  administrator to carry — said on the screen rather than left to be discovered.
- **`GET /me`**, outside the pipeline for the same structural reason as sign-in: every operation
  names a workspace and is authorised against membership of it, so "which workspaces am I in" has
  no workspace to name.
- **Three new permissions** — `ManageProjects` and `ManageAccounts` for administrators,
  `ManageWorkItems` from contributor upward, because registering the work a draft is about is part
  of contributing rather than of administering.

What landed in the browser, in `web/admin`:

- **Vite, React, TypeScript, Tailwind, TanStack Query, React Router**, built and tested with Bun.
  The handful of UI primitives are written here rather than pulled from a component library: the
  whole visual surface is a form, a table, and a panel.
- **A generated client.** `GET /operations` carries a JSON schema for every operation's arguments
  and result, and `web/admin/src/api/operations.ts` is generated from it. A test regenerates and
  compares, so an operation added, renamed, or reshaped without regenerating fails in CI rather
  than in a browser. It found its first drift within the hour, which is the point of it.
- **The approval screen shows the exact revision and its hash, and submits that hash.** There is
  no path from the UI to "approve the latest". A smoke test asserts the request body carries the
  hash that was on screen.
- **Role-driven navigation**, built from the permissions `/me` reports. A viewer is offered neither
  membership administration nor the audit trail; the server refuses both regardless, and that is
  what the integration tests prove.

Verified by actually running it: PostgreSQL in Docker, migrations and bootstrap through the
console, the API, and the dev server, driven in a browser. Sign in, workspace, projects with the
AI policy shown as state, enabling AI access, membership administration, onboarding a second
person and seeing their setup token, registering a work item, component health, and an audit trail
showing the actions just performed with no content in them. Running it found a real bug that no
test had: the servers ignored the `DEVBUDDY_` environment prefix the console reads, so a
deployment configured one way started the console and left the API insisting no connection string
existed.

Known gaps, carried forward rather than papered over:

- **Additional workspaces cannot be created.** The bootstrap makes the first one; creating another
  needs an installation-level role that v1 does not have, and inventing one is a tenancy decision
  for the project owner rather than something to slip in. info.md asks for the simplest
  single-workspace experience first, and everything inside a workspace is administrable.
- **Teams have no operations.** The entity exists and nothing reads or writes it. Membership is
  granted per person, workspace-wide or per project, which covers the access model; teams are a
  grouping nobody can yet create.
- **The refresh token lives in `sessionStorage`.** The server issues bearer tokens rather than
  setting an HttpOnly cookie, so the browser has to hold one somewhere. Recorded against SB-14;
  the fix is a cookie-based refresh, which is a server change.
- **Rate limiting still covers the credential endpoints only**, and there is no concurrent-job cap
  (SB-21, Phase 10).
- **Recovery and setup tokens are still written to the log and handed to an administrator**,
  because no delivery channel exists.

---

## Phase 9 — Claude and Codex plugin packages

**Goal:** two thin, separate packages over the same MCP server — no duplicated core logic and no
per-AI implementations.

- `plugins/claude/`: `.claude-plugin/plugin.json`, MCP server configuration, slash commands for
  the common flows (search knowledge, analyze change impact, draft a record, generate handover),
  and a skill carrying the usage and safety instructions.
- `plugins/codex/`: `AGENTS.md` instructions plus the `config.toml` MCP server entry.
- Both point at the same `DevBuddy.McpServer`. Instructions differ; capability does not.
- Plugin instructions state plainly that prompt text is not a security boundary — the server
  enforces scope regardless of what any instruction file says.
- Document the required host-side file-access boundaries: MCP filtering does not govern the AI
  host's direct filesystem access or its other tools, so the host must be configured separately.

**Exit criteria:** a manual walkthrough in both Claude Code and Codex performs search → analyze →
create draft against a local instance, and the resulting draft appears in the web UI awaiting
human approval.

**Status: COMPLETE (2026-09-03).** Both exit criteria are met. The walkthrough ran end to end in
**both** hosts against a local instance, each in its own bootstrapped installation:

| | Claude Code | Codex |
|---|---|---|
| `list_projects` | Alpha, AI access enabled | Alpha, AI access enabled |
| `search_knowledge` | 0 hits | 0 hits |
| `analyze_project` | 2 files, 372 B | not called |
| `create_draft` | `540abb20` | `c95898dd` |

Both drafts appear in the web UI as a Draft awaiting a person, provenance `AiDraft`,
`isAiGenerated` true, with the content hash shown and "No approval covers this revision". Neither
host could reach an operation people alone may perform, because neither was ever told one exists.

367 .NET tests and 23 web tests pass, 0 warnings, formatting clean.

**A hole was found and closed first, and it was ours.** The MCP stdio server took its caller
identifier from `DEVBUDDY_ACTOR`, an environment variable. Anybody who could start the process
could start it as anybody. That had never mattered because nothing launched it; Phase 9 is the
phase that ships configuration telling people to launch it, so shipping the packages on top of it
would have shipped impersonation as documented configuration.

What landed:

- **Machine tokens.** A long-lived, revocable credential a process presents instead of signing in:
  256 bits of randomness, stored as a SHA-256 digest, named, expiring, resolved against the
  database on every call so revocation is immediate rather than effective at the next restart. A
  separate table from refresh tokens on purpose — that table's invariant is that a token is used
  exactly once, which is what makes a second presentation mean theft, and a row meant to be
  presented daily would have quietly retired it.
- **It grants nothing.** A token carries exactly its owner's permissions: the same memberships,
  roles, and per-project AI policy. `ManageOwnCredentials` is held by every role including Viewer,
  because refusing it would only mean a viewer could read knowledge in a browser and not from the
  editor they work in. Three operations — issue, list, revoke — all acting on the caller and
  nobody else, all denied to AI.
- **A Plugin access screen** in the web UI, offered to everybody, showing the token once.
- **Two packages.** `plugins/claude/` with a manifest, an MCP configuration, four slash commands,
  and a skill; `plugins/codex/` with `AGENTS.md` and a `config.toml`. Instructions differ,
  capability does not.
- **The packages are pinned by tests.** A shipped instruction file may not so much as name an
  operation people alone may perform — not as an example, not to say it is unavailable, because a
  name in a file an assistant reads is a name it now knows to try. Both files must describe exactly
  the eighteen exposed tools, both must launch the same server over stdio, both must configure a
  token and never an actor identifier, and both must still say that prompt text is not a security
  boundary and that the tool boundary is not the host's boundary.
- **The stdio transport is exercised for real.** `StdioTransportTests` launches the host process
  and speaks MCP over the pipes; `StdioPluginTests` does the same against real PostgreSQL and
  proves the three that matter: without a token nobody is anybody, with one the caller is exactly
  its owner, and revoking one stops it on the next call.
- **`docs/operations/plugin-hosts.md`**, including the part that gets assumed: MCP filtering governs
  DevBuddy and nothing else. The assistant's own file reads and shell commands go straight past it,
  and an operator who reasons "DevBuddy would have refused" is wrong — the assistant can `cat` it.

Running it found two things tests had not:

- **The session died on every page reload.** React's development double-mount fired two refreshes
  at once, the second presented a token the first had just spent, the server correctly read that as
  theft and revoked the family. Refreshing is single-flight now, and the guard is
  mutation-checked — it is not a development-only problem, since any page with several queries can
  get several 401s at the same moment.
- **A record with nothing published displayed "published: undefined"**, because the server omits
  null fields rather than sending them and the check was against null alone.

Two host-side details worth recording, because both cost time to find:

- **Codex refuses MCP tool calls in non-interactive mode** unless approvals are routed somewhere.
  `codex exec --approve-for-me` is the documented way; without it every call comes back "requires
  approval, but approval policy is never", which looks like a server refusal and is not one.
- **Claude Code needs the tools named**, as `--allowedTools
  mcp__devbuddy__search_knowledge,...`, or it will not call them in print mode.

Both are in `docs/operations/plugin-hosts.md`.

---

## Phase 10 — Packaging, hosting, and supply chain

**Goal:** self-contained artifacts and Docker images on user-controlled infrastructure.

- Self-contained publish for the console and server workloads, with an explicit **RID and
  verification matrix** in `docs/operations/release-matrix.md`. Short and honest, three tiers
  (**amended 2026-09-07**, to what `v1.0.0` shipped; the amendment note is in `info.md`):

  | Tier | RIDs | Meaning |
  |---|---|---|
  | Verified | `linux-x64`, `linux-arm64`, `win-x64`, `linux-musl-x64` | Built **and** smoke-tested by hand, recorded per release. CI runs the full suite on `ubuntu-latest` and `windows-latest` only. |
  | Built, unverified | `osx-arm64`, `osx-x64`, `win-arm64`, `linux-musl-arm64` | Published as-is; never run. Stated as unverified in the release notes. |
  | Not published | everything else | Out of scope for v1; not claimed as supported. |

  Native OS dependencies are documented explicitly (ICU and OpenSSL on Linux unless invariant
  globalization is enabled). Self-contained does **not** imply single-file or Native AOT, and no
  universal-compatibility claim is made. Recheck against the .NET 10 supported-OS list at release.
- Docker images for API, MCP, and CLI; `compose.yaml` with PostgreSQL and MinIO.
  **Container-platform matrix is separate** from the native matrix: `linux/amd64` only
  (**amended 2026-09-07**; `linux/arm64` was in this line and is not built), on chiseled ASP.NET
  base images.
- Hardening: non-root containers, no Docker socket mount, database not published to the host
  network, TLS termination documented, secrets via environment/secret store — never baked into images.
- Persistence: named volumes for database and evidence; a documented and *tested* restore path
  proving data survives container replacement.
- Supply chain: pinned dependency versions via central package management, SBOM generation,
  vulnerability scanning in CI, and a documented release/update process for plugins, executables,
  and images.
- Availability limits: max upload size, analysis time limits, request rate limits, and concurrent
  job caps.

**Exit criteria:** `docker compose up` from a clean machine reaches a healthy system; a
destroy-and-restore drill recovers all records and evidence; the release matrix lists only
combinations that were actually built and verified.

**Status: COMPLETE (2026-09-03).** All three exit criteria are met, each by doing the thing rather
than describing it. Six controls reach `TESTED` — SB-21, SB-28, SB-30, SB-31, SB-32 and SB-33 —
taking the total to 30 of 33, and Phase 11 scenarios 6 and 7 are now exercised. 369 .NET tests and
23 web tests pass, 0 warnings, formatting clean.

What landed:

- **Three container images**, on chiseled bases: no shell, no package manager, no busybox. If
  something gets code execution in one of these there is nothing in it to execute — which is also
  why the API implements `--health-check` as a mode of itself, because there is no curl to run.
- **A Compose stack** whose absences are the design. The database and the object store publish no
  ports at all; what is published is bound to loopback, because this expects a reverse proxy in
  front of it; nothing mounts the Docker socket; nothing runs as root; the application containers
  are read-only with every capability dropped; the working copy is mounted read-only.
- **Backup and restore, both halves.** Rows *and* artefacts — a backup of only the database is the
  mistake this is shaped to avoid, because a restore that returns the rows and leaves the bytes
  behind looks like a success until a reader clicks an attachment. Logical rather than
  `pg_dump`, because running an external program from product code would put process APIs into an
  assembly a build-breaking test scans to keep them out (SB-04). Sessions are deliberately not
  restored; passwords and machine tokens are.
- **The drill, twice.** `RestoreDrillTests` destroys a database — a different one, migrated from
  nothing, because deleting rows leaves everything a migration created — restores, and checks the
  record, its approval hash, the account, the audit entry, and the evidence bytes read back out of
  MinIO. Then the same procedure by hand against the Compose stack.
- **The limits that were missing.** A global per-caller request ceiling, a process-wide cap on
  concurrent analyses enforced by a decorator around the port, and a request body ceiling.
- **Supply chain in CI.** `dotnet list package --vulnerable --include-transitive` on every push and
  weekly — weekly because a vulnerability is published against a dependency that has not changed —
  CycloneDX SBOMs per shipped host, and an image check that fails if any runs as root.
- **A release matrix that only claims what was run.** Eight RIDs build; four were actually started
  and answered, one of those under emulation, and the table says which is which. The native
  dependencies are listed because "self-contained" misleads people: a bare `ubuntu:24.04` fails at
  startup without `libicu`, and a bare `alpine:3` without `libstdc++`, `libgcc` and `icu-libs`.
  Both observed, not assumed.

Standing the stack up found three real defects that no test had, which is the argument for this
phase being work rather than paperwork:

- **The MCP server's HTTP transport had never once started.** `MapMcp` was called without
  `WithHttpTransport`, so every start in HTTP mode threw at startup. Phase 7 shipped "two
  transports" and only stdio had ever run, because stdio is what the plugins use.
- **The backup volume arrived owned by root** and the first backup failed with permission denied.
  A chiseled image has no shell, so the mount point is created and chowned in the build stage and
  copied in — Docker seeds a fresh named volume from the mount point's ownership.
- **`restore_system` could never have succeeded.** Every operation is authorised against a
  membership, and a restore from total loss runs against a database with no memberships in it: the
  caller does not exist yet, so the pipeline refuses. It refuses a populated database too, by
  design. That leaves no state in which it could work, so it is gone from the catalogue and
  restore is a console command, outside the pipeline, like migrate and the bootstrap. A test
  asserts no restore operation exists, so it cannot come back without somebody meeting the reason
  it went.

Known gaps, carried forward:

- **SB-29 was `IMPLEMENTED`, not `TESTED`, when this phase closed.** SBOMs were generated but no
  release had carried one, and nothing was signed. It moved on 2026-09-06, when `v1.0.0` was
  published with an SBOM per image and keyless OIDC attestations, verified from outside the
  workflow that made them.
- **The image half of SB-32 is by construction rather than by scan.** No `COPY` brings a secret in
  and no `ENV` sets one, but no image scanner runs, so a secret introduced another way would not be
  caught.
- **`linux/arm64` images are not built.** The Dockerfiles have nothing architecture-specific in
  them, but until a multi-platform build runs, the row would be a guess.
- **Backup retention is recorded and not enforced.** `Backup:Retention` says how long backups are
  meant to be kept; deleting them on that schedule is an operator's job until SB-27 lands. Closed
  in Phase 11: `dotnet run -- retention` now enforces it, on the operator's own schedule.
- **Npgsql logs a missing `libgssapi_krb5.so.2`** on every chiseled container. Harmless, and
  recorded in the release matrix because it reads like a failure.

---

## Phase 11 — Security verification and release readiness

**Goal:** evidence that controls work — not documentation that they exist.

Run the full verification set required by `info.md` and record results and gaps in
`docs/security/verification-matrix.md`:

1. Cross-user, cross-team, and cross-project access — including search, caches, attachments, exports.
2. Revoked permissions take effect immediately for subsequent access.
3. Malicious source and tool content (prompt injection) produces no unauthorized action.
4. Sensitive-data leakage: secrets and personal data blocked before retention *and* before egress.
5. Approval revision mismatch is rejected.
6. Resource exhaustion: oversized files, long-running analysis, request floods, concurrent jobs.
7. Backup, restore, and incident detection/response procedures exercised.
8. Retention and deletion applied to logs, backups, exports, and caches — not only the primary database.

### Retention defaults

Configurable per deployment; these are the shipped defaults, and each one has a scheduled purge
job plus a test that proves data is actually gone from that copy.

| Data | Default retention | Notes |
|---|---|---|
| Published record revisions | Indefinite | Knowledge integrity; superseded revisions are kept, never rewritten. |
| Archived records | 24 months after archive | Then eligible for deletion on owner request. |
| Draft records | 12 months untouched | Then flagged stale, not auto-deleted. |
| Evidence objects | Life of the linked record + 90 days | Orphaned objects purged on a weekly sweep. |
| Audit events | 24 months | Then deleted. Audit never stores the sensitive payload accessed. |
| Application logs | 90 days | Redacted at write time, not at read time. |
| Exports | 30 days | Object purged and download links invalidated. |
| Backups | Daily kept 30 days; monthly kept 12 months | Restore drill runs each release. |
| Search index and caches | Derived, rebuildable; cache TTL ≤ 15 minutes | Invalidated immediately on permission change. |
| Deleted project | Hard purge within 30 days | Database, evidence, exports, and caches. Backups age out on their own schedule — this lag is documented as a known residual risk, not hidden. |

Then write a release-readiness note stating, per control: implemented, tested, evidence link,
residual risk. Anything untested is stated as untested.

**Exit criteria:** the matrix has no silent gaps; every remaining risk is named and accepted
explicitly by the project owner before real project data is connected.

**Status: COMPLETE (2026-09-03).** SB-18 and SB-27 were the two rows still `NOT IMPLEMENTED`
entering this phase; SB-18 now reaches `TESTED` and SB-27 reaches `IMPLEMENTED`, taking the matrix
to 31 `TESTED`, 2 `IMPLEMENTED`, and zero `NOT IMPLEMENTED`. `docs/security/release-readiness.md`
is the note the exit criteria calls for. 405 .NET tests exist; 364 pass in this environment and
41 are blocked by an unrelated host issue, detailed below.

What landed:

- **SB-18, the customer, production, and personal-data policy.** A second scanner and redactor,
  built the same way SB-17's are — one rule set shared by both, so nothing reported as sensitive
  is released anyway — covering a US SSN, a Luhn-checked payment card number, and labelled fields
  such as a date of birth or a passport number. The pipeline applies it only on the AI channel,
  and only when the project's AI access policy carries no approved bounded scope: `BoundedDataScope`
  now travels on the authorization decision, read once when the AI policy is checked rather than
  queried a second time. A bounded scope excuses personal data and nothing else — a secret is
  still refused inside one, per `info.md`. `PersonalDataPolicyTests` proves the gating against
  fakes; `PersonalDataChannelTests` proves the same end to end against real PostgreSQL, including
  that the human channel is never subject to the control at all.
- **SB-27, retention enforcement, for the copies this build can reach.** `dotnet run -- retention`,
  a new console command outside the pipeline for the same reason `restore` is: a sweep spans every
  workspace and project, and there is no single caller to authorise it against. One pass deletes
  audit events past a 24-month window, deletes evidence no revision references once a 90-day grace
  period passes (from both the database row and the object store), and deletes a backup directory
  once it is older than `Backup:Retention` — closing the exact gap the Phase 10 section above
  named as recorded but not enforced. An archived record past its window is reported eligible, not
  deleted, matching the schedule's "on owner request." `RetentionEnforcementTests` proves all four
  behaviours against real PostgreSQL and the real filesystem.
- **Three rows of the retention schedule remain unimplemented, named rather than hidden.** Exports
  carry no stored artefact yet — `ExportAsync` returns a manifest and writes nothing to disk — so
  there is nothing to purge until that changes. Application log retention is a container log-driver
  setting (`docker/compose.yaml` now bounds it by size), not application code a test can assert on.
  A deleted-project sweep has no trigger, because no operation in this build deletes a project at
  all. `docs/security/release-readiness.md` asks the project owner to accept these three explicitly.

**An environment issue during this phase, resolved without a code change:** the
DevBuddy.Infrastructure.Tests assembly's own copy of `Docker.DotNet.Handler.Abstractions.dll` was
blocked for a time by this machine's Application Control policy, failing the Testcontainers-dependent
tests in that project. The identical Testcontainers usage in DevBuddy.Security.Tests and
DevBuddy.Api.Tests ran reliably throughout, which is why this phase's Postgres-backed verification
(`PersonalDataChannelTests`, `RetentionEnforcementTests`) was written there instead of in
Infrastructure.Tests. By the time the gap-closing work below finished, the block had cleared on its
own and the full DevBuddy.Infrastructure.Tests suite passed again — a host security policy, never a
code path this project controlled.

**Phase 11 is closed, and with it v1 (2026-09-06).** The status above is what was true on
2026-09-03 and is kept that way; this paragraph is what closed the distance between it and the
exit criteria. All three residual retention rows named above are gone rather than accepted:
`export_project` writes an actual copy, `delete_project` purges a project immediately, and
application log retention is application code with a test against it — the sections below record
each. SB-29, the last row that was `IMPLEMENTED` rather than `TESTED`, moved when `v1.0.0` was
published, signed, and verified from outside the workflow that built it.

**The matrix reads 33 `TESTED`, 0 `IMPLEMENTED`, 0 `NOT IMPLEMENTED`, and all eight scenarios are
exercised.** The exit criteria are met: there are no silent gaps, and what remains is named in
`docs/security/release-readiness.md` for the project owner to accept — four platforms built and
published without ever being run, no `linux/arm64` image, and the operator-side facts about
application logs, chiefly that with no SMTP configured setup and recovery tokens are written to
them by design.

---

## The evidence store had no write side

**Status: CLOSED (2026-09-05).** Found while writing the restore drill's own instructions, by
checking whether a step could actually be performed rather than assuming it could.

`IEvidenceStore.StoreAsync` was called by no operation, no endpoint and no screen — only by tests.
Everything around it was complete: `download_evidence` streamed bytes after an authorization
check, backup and restore carried them, `export_project` copied them, retention swept orphans, and
the tenant isolation suite covered attachments. All of it operated on evidence that test code had
seeded directly, which is exactly why nine phases went by without anyone noticing that a real
installation could never have had an artefact to download.

`capture_evidence` and `list_evidence` close it. Three decisions worth keeping:

- **The scan happens before the store is touched.** The request carries an array rather than a
  stream so the pipeline's own scanner sees the content on the way in, which is what lets a file
  holding a credential be refused with nothing written — the same shape SB-17 already had for
  drafts, rather than a store-then-delete that a crash could interrupt.
- **`RecordScanResultAsync` moved onto the port.** The implementation had existed since Phase 3
  with nothing able to call it. Stored evidence begins `NotScanned` and the download refuses to
  release anything in that state, so a capture that skipped it would have written bytes nobody
  could ever read back.
- **Capture and download have routes of their own; listing is dispatched.** Base64 in a JSON
  envelope inflates a file by a third and puts it through the serialiser. Naming what a project
  holds is ordinary JSON and goes through the manifest like everything else.

Human-only in both directions, and the AI-channel test pins it: a channel that could push bytes
into the evidence store would be a way around SB-17 rather than a use of it.

---

## Closing the v1 gaps named at the end of Phase 11

**Status: COMPLETE (2026-09-03).** The Phase 11 release-readiness note accepted three residual
risks against SB-27 (retention) pending further work, and CLAUDE.md separately named four
functional v1 gaps that were true but not security-control failures: only one workspace could
ever be created, teams had no operations, a setup or recovery token had no real delivery channel,
and source synchronisation could not read pull requests, issues, or review threads. All are closed.
405 .NET tests grew to 433 and 23 web tests to 31; all of them pass. The matrix reaches 32
`TESTED` of 33, with SB-29 the sole `IMPLEMENTED` row, unchanged, because it needs an actual
release to prove — which it got on 2026-09-06, taking the matrix to 33 of 33.

What landed:

- **`delete_project`** removes a project's work items, records with their full revision history,
  evidence rows and bytes, source repositories, and project-scoped memberships, immediately.
  Audit history survives the project it describes. This is also what closes SB-27's deleted-project
  residual: an immediate delete satisfies "hard purge within 30 days" trivially, so the sweep that
  control once lacked a trigger for is unneeded rather than missing.
- **`export_project` writes an actual copy** — records, work items, and evidence bytes — through a
  new `ExportService` structured like `BackupService`, scoped to one project. This closes SB-27's
  other residual: `dotnet run -- retention` now purges an export directory past its window the same
  way it purges a stale backup.
- **Team administration**: `create_team`, `rename_team`, `delete_team`, `list_teams`,
  `list_team_members`, `add_team_member`, `remove_team_member`. The entity and its table existed
  since Phase 1 with nothing reading or writing them. A team still carries no permission of its
  own — `Membership` decides what anyone may do, unchanged.
- **`create_workspace`**, sponsored by a workspace the caller already administers and authorised
  against it through the ordinary pipeline — not a new installation-wide role, which would have
  been a materially different security model from everything else here. `IInstallationBootstrapper`
  is untouched: it still makes the first workspace once, on an empty database, and still refuses
  afterwards.
- **`IEmailSender`** replaces the ad hoc paths that used to hand a setup or recovery token to
  whoever was already looking. `EmailOptions.Provider` defaults to `Log` — the same "an operator
  completes this by hand" behaviour this system always had, fixed rather than only kept: the
  previous code logged a warning claiming a recovery token was written to the log without the
  token actually being in it. `Smtp` (MailKit) delivers it for real once an operator configures a
  server.
- **GitHub API source synchronisation**, opt-in via `GitHubOptions.Mode = GitHubApi`, behind the
  same `ISourceSystemClient` port the working-copy reader already used — exactly the extension
  point its own doc comment predicted. `sync_sources`, `compare_snapshots`, and
  `analyze_change_impact` all gain a live-API path with no catalogue or AI-surface change, since
  they already depended on the port rather than the implementation. Pull requests, issues, and
  review comments are newly readable; a mounted working copy still refuses those questions rather
  than answering them as empty. Off by default, and inert until an operator both sets a token and
  adds `api.github.com` to `OutboundAccess:AllowedHosts` — proven by a test asserting the request
  never reaches the transport when that host is absent.
- **The administration UI for all of it.** The three new human-facing capabilities were reachable
  over HTTP but had no screen, which left `info.md`'s "workspace/team/project and membership
  administration" half-met: a person could not do from a browser what the API now allowed. Closed
  with a `Teams` screen (create, rename, delete, and staff a team, picking people from the
  workspace's own members rather than asking for an identifier), a `Workspaces` screen (the
  workspaces you can reach, and standing up another sponsored by this one), and project deletion
  on the project list behind typing the project's name back — a confirmation somebody can click
  through is not a confirmation when there is no undo. Both new nav entries are gated on their
  permission, so a viewer is offered neither, and a test asserts that.

Two bugs were found and fixed along the way, neither previously covered by a test: `RenameTeamUseCase`
first attempt threw an EF Core identity-conflict, because updating a row read with `AsNoTracking`
requires fetching the tracked instance rather than attaching a fresh one — the same pattern
`AccessDirectory.UpdateMembershipAsync` already used elsewhere, now matched. And the exact
recovery-token logging bug named above: `RecoveryLog.TokenIssuedWithoutDelivery` had no parameter
for the token at all, so the log line it wrote never contained what it claimed to.

`docs/security/release-readiness.md` reflects the closures in full; `docs/security/verification-matrix.md`
records SB-27 at `TESTED`.

---

## v1 is released

**Status: COMPLETE (2026-09-06).** Phases 0 to 11 are closed, the gaps named after Phase 11 are
closed, and `v1.0.0` is published: built from `9a8ebf0` by `.github/workflows/release.yml`, run
34045844221. Three images in GHCR with an SBOM each, every archive signed with GitHub's keyless
OIDC identity, and the attestations verified after publication from outside the workflow that made
them — against a negative control, so that a pass means something. That was the last thing SB-29
needed, and the matrix now reads **33 `TESTED` of 33**.

What "complete" is being claimed to mean, so a later owner can check it rather than trust it:

- **Every operation a person needs is reachable from `web/admin`**, and the AI surface is a
  deliberate allow-list of eighteen operations over the same use cases, denied by default per
  project.
- **433 .NET tests and 31 web tests pass**, including the eight security scenarios `info.md`
  requires, each mapped to a row in the verification matrix.
- **A release is reproducible from a tag and verifiable by somebody who does not trust this
  repository**, which is the only version of "signed" worth having.
- **The restore drill has been performed by hand**, not asserted: an artefact came back byte for
  byte after the database volume was destroyed.

What is **not** claimed, stated here rather than left to be discovered:

- Four platforms — `osx-arm64`, `osx-x64`, `win-arm64`, `linux-musl-arm64` — were built and
  published **without ever being run**. No macOS and no Windows on ARM was available. Whoever
  deploys on one of them is the first to run it.
- `linux/arm64` container images are not built and not claimed.
- Retention windows are enforced by `dotnet run -- retention`, which runs on whatever schedule the
  operator provides. Nothing inside this system guarantees it runs at all.
- With no SMTP configured, setup and recovery tokens are written to the application log **by
  design**. A retention window is not a control over who can read that file while it exists.

The last two are the operator's, and `docs/security/release-readiness.md` asks the project owner to
accept them in writing before real project data is connected. That acceptance, not this section, is
the gate on using this system for real work.

---

## Phase 12 — Post-v1: operational closure and the two deferred decisions

**Status: DRAFT (2026-09-07). Proposed, not approved, and not recorded in `info.md`.** Phases 0 to
11 were approved as a sequence before any of them started. This one is written after v1 shipped,
so it does not inherit that approval, and nothing in it should be built until the project owner
confirms it the same way. Two of its three tracks carry decisions the owner has to *make* rather
than inherit, which is precisely why they were kept out of v1.

**Goal:** take back from the operator what code can hold, settle what happens to the platforms v1
published without ever running, and decide the two capabilities `info.md` deferred — without
moving any of the 33 `TESTED` rows backwards.

### 12A — The acceptance that gates real project data

No code. `docs/security/release-readiness.md` asks the project owner to accept two things in
writing before real project data is connected, and `info.md` records no such acceptance today. The
gate is therefore open in fact and closed only on paper, which is the one state this project set
out not to be in.

- **The operator-side facts about application logs.** Retention is enforced and tested, but three
  things about it are outside this code: whether anything runs `dotnet run -- retention` on a
  schedule at all, whether `Logging:File:Path` stays set (clearing it reverts to the size-bounded
  log driver), and — the one that matters most — that with no SMTP configured, setup and recovery
  tokens are written to those logs **by design**. A retention window is not a control over who can
  read the file while it exists. The acceptance should name which option from
  `docs/operations/logging.md` is in force and who can read the volume.
- **What `v1.0.0` published without running.** Four platforms — `osx-arm64`, `osx-x64`,
  `win-arm64`, `linux-musl-arm64` — were built and shipped and have never been started, and no
  `linux/arm64` container image is built at all.

**Exit criteria:** a dated, owner-confirmed entry in `info.md` naming both, in the same form as the
2026-09-06 and 2026-09-07 entries. Until that entry exists, the other two tracks are optional and
this one is not.

### 12B — Operational closure

Packaging, CI, and console paths only. Nothing here adds an operation, changes the AI surface, or
touches a security control, which is what makes it separable from 12C and safe to do first.

- **A retention runner that ships with the stack.** `docker/compose.yaml` has `migrate`, `api`,
  `mcp`, `database` and `evidence` and no scheduler, so today the sweep runs only if an operator
  builds one. The constraint that shapes the answer: the images are chiseled — no shell, no cron —
  so a sidecar running `cron` would need a shell-bearing image built for the purpose. The
  alternative is a scheduling mode on the console itself (`retention --every`), which keeps it in
  the one image that already carries the code. Either way it must stay **outside** the pipeline for
  the reason it was put there: a sweep spans every workspace and project and has no caller to
  authorise it against, exactly like `restore`. A scheduler that acquired a caller would be the
  installation-wide superuser this system does not have.
- **`linux/arm64` container images.** A multi-platform build, not a code change — the Dockerfiles
  contain nothing architecture-specific. The row in `docs/operations/release-matrix.md` moves only
  when an arm64 image has actually been *started*, not when one has been built, which is the same
  standard the native matrix already holds.
- **The four unrun platforms.** A decision, not work: acquire the hardware and verify them, or stop
  publishing them. Shipping an artefact nobody has ever run is defensible once, stated plainly in
  the release notes as `v1.0.0` did. It is harder to defend the second time.
- **The next release re-runs its checklist in full.** `v1.0.0` carried its smoke tests over from
  `6a60a48`, which was sound only because the commits between it and `9a8ebf0` changed no product
  code. That will not be true of the next tag. `docs/operations/release-matrix.md` records per
  release what was re-run and what was carried; a release that carries everything is not a verified
  release.
- **A decision on the `Log` email provider.** Three options, and the choice belongs to the owner
  because none of them is free: leave it as it is (documented, accepted in 12A), require an
  explicit opt-in before a token is ever written to a log, or refuse to start outside development
  without a delivery channel. The third closes the risk properly and breaks a plain `docker run`
  for a first-time operator, which is why it is a decision rather than an obvious fix.

**Exit criteria:** the shipped stack schedules the retention sweep and a test proves the schedule
invokes the same sweep the console command does; `linux/arm64` is either published *and started*,
or explicitly not claimed with the reason recorded in ADR-0008; and the release-matrix tiers for
the next release describe only what was run for that release.

### 12C — The two deferred capabilities

Both require an ADR and the project owner's separate approval before any code, per the AI Data
Policy in `info.md`. Neither is a continuation of v1: each opens a boundary v1 does not have.

- **Embeddings and vector search.** `info.md` defers these until an embedding model or provider is
  selected and separately approved, and requires any vector index to be a **derived** search index,
  never a source of truth. What an ADR has to settle beyond the provider: embedding text is
  **egress** — a third path out of the boundary, alongside the AI channel and the telemetry
  exporter — so SB-17 and SB-18 have to apply to it before text leaves, and the verification matrix
  gains its own rows rather than being read as covered by existing ones. And because the retention
  schedule already calls embeddings and caches derived and rebuildable, a permission change must
  invalidate the index the way it invalidates a cache: an index that outlives a revoked membership
  is a cross-tenant leak wearing a different shape.
- **`knowledge-ai-worker`.** Autonomous background work — scheduled source analysis, embedding
  generation, stale-record detection, recurring reports. The design problem to solve before the
  features is the one that removed `restore_system` and kept `retention` out of the pipeline: **a
  background job has no caller to authorise it against.** Either the worker holds a machine token
  with a real membership and is bounded by it exactly like any other caller, or it runs outside the
  pipeline like `retention` and may therefore touch nothing a person's permissions would gate.
  Anything between those two reintroduces the installation-wide superuser this system has
  deliberately never had. Human approval before any output is published stays, per `info.md`, as do
  authentication, cost, permissions, and provenance.

Three things stay out, restated here so a later owner does not read their absence as an oversight:
a web chat (ruled out in Phase 8 — AI questions stay in Claude and Codex), organisation login or
enterprise SSO (`info.md`), and writing back to GitHub (source synchronisation is one-way by a
confirmed decision, not by omission). Sandboxed repository execution stays out too: SB-04 permits
it only as a separately authorised, isolated feature, and neither v1 nor this proposal asks for one.

**Exit criteria:** an ADR per capability, confirmed in `info.md`, before a line of either is
written. No implementation exit criteria are proposed here on purpose — the shape of that work
depends on decisions nobody has made yet, and inventing criteria for it would be exactly the
paper-ahead-of-evidence this plan exists to avoid.

---

## Critical files this plan creates first

| File | Why it matters |
|---|---|
| `docs/plan.md` | Committed companion to `info.md`; the phase sequence of record. |
| `docs/security/verification-matrix.md` | The only place that says whether a control is really tested. |
| `Directory.Build.props` / `Directory.Packages.props` | Pins language version, analyzers, and dependency versions across every project. |
| `src/core/DevBuddy.Domain/WorkItemType.cs` | Where `ChangeRequest` and `CodeReview` are kept distinct; anchors the `CR` ban. |
| `src/core/DevBuddy.Application/Ports/` | The seam that keeps the core independent of persistence, transport, and AI provider. |
| `src/hosts/DevBuddy.McpServer/ToolSurface.cs` | The explicit AI allow-list; a test pins it. |

---

## Verification

Per-phase gates run in CI on every change:

```bash
dotnet build -c Release
```

```bash
dotnet test -c Release
```

Test tiers and what each proves:

- **Domain tests** — invariants: no publish on a stale approval, provenance required, scoping required.
- **Architecture tests** — `Domain` has no outward references; hosts do not bypass `Application`;
  the token `CR` does not appear as a type/enum/column identifier.
- **Infrastructure tests (Testcontainers PostgreSQL)** — migrations apply to an empty database,
  aggregates round-trip, and FTS + filters never cross a project boundary.
- **API integration tests (`WebApplicationFactory`)** — auth flows, role-gated endpoints, and
  isolation across search/export/attachment paths.
- **MCP tests** — the exported tool list equals the allow-list exactly; AI-denied projects return
  nothing; per-user narrowing holds.
- **Security tests** — the eight Phase 11 scenarios, each mapped to a row in the verification matrix.

End-to-end manual verification, once Phase 10 lands:

```bash
docker compose -f docker/compose.yaml up -d
```

Then: register the first administrator through the web UI → create a workspace and project →
enable AI access for that project only → from Claude Code and from Codex, run search → analyze →
`create_draft` → approve the draft in the web UI → confirm the published record, its provenance,
and the audit entry naming the approver and the exact approved revision → attempt the same query
against a second project with AI access disabled and confirm nothing is returned.

---

## Resolved design decisions (confirmed 2026-09-01)

These were the plan's open questions. All five are now settled and folded into the phases above;
each gets an ADR in `docs/adr/`.

1. **Evidence store (Phase 3)** — **MinIO** is the default store and ships in `compose.yaml`. The
   filesystem adapter stays behind the same `IEvidenceStore` port for tests and single-host installs.
2. **MCP transport (Phase 7)** — **both** ship in the first release: stdio for the local Claude and
   Codex plugins, authenticated HTTP for self-hosted/remote. One tool allow-list covers both.
3. **RID matrix (Phase 10)** — three tiers, **amended 2026-09-07** to what `v1.0.0` shipped:
   verified (`linux-x64`, `linux-arm64`, `win-x64`, `linux-musl-x64`), built-but-unverified
   (`osx-arm64`, `osx-x64`, `win-arm64`, `linux-musl-arm64`), and not published. Containers are a
   separate matrix: `linux/amd64` only. The amendment note is in `info.md`.
4. **Retention (Phase 11)** — the defaults table in Phase 11 applies, covering records, evidence,
   audit, logs, exports, backups, and caches, with the backup-lag residual risk stated explicitly.
5. **Work-item source of truth (Phase 6)** — **DevBuddy owns the work item.** GitHub is imported
   read-only as linked references and snapshots, never written back, so `SyncSources` is one-way.

## Remaining open items

None blocking, and none belonging to v1: it is released, and what it does not claim is listed under
*v1 is released* above. Anything discovered during a phase was raised before that phase's exit
criteria were claimed, and recorded as an ADR rather than decided silently in code. The same
standard applies to whatever follows this release — including the two things a next version would
have to decide rather than inherit: whether `linux/arm64` images and the four unrun platforms are
worth the cost of keeping working, and what a v2 does about the embeddings and vector search this
plan deliberately kept out of v1.

Both of those, and the acceptance that gates real project data, are now drafted as **Phase 12**
above. That section is a proposal: it is not approved, it is not in `info.md`, and no work in it
has started.
