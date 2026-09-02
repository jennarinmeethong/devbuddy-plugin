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
- **Observability:** Serilog structured logging + OpenTelemetry traces/metrics; audit is a
  separate, append-only store — not application logs.
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

---

## Phase 10 — Packaging, hosting, and supply chain

**Goal:** self-contained artifacts and Docker images on user-controlled infrastructure.

- Self-contained publish for the console and server workloads, with an explicit **RID and
  verification matrix** in `docs/operations/release-matrix.md`. Short and honest, three tiers:

  | Tier | RIDs | Meaning |
  |---|---|---|
  | Verified | `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64` | Built **and** smoke-tested in CI each release. |
  | Built, unverified | `win-arm64`, `osx-x64`, `linux-musl-x64`, `linux-musl-arm64` | Published as-is; no automated verification. Stated as unverified in release notes. |
  | Not published | everything else | Out of scope for v1; not claimed as supported. |

  Native OS dependencies are documented explicitly (ICU and OpenSSL on Linux unless invariant
  globalization is enabled). Self-contained does **not** imply single-file or Native AOT, and no
  universal-compatibility claim is made. Recheck against the .NET 10 supported-OS list at release.
- Docker images for API, MCP, and CLI; `compose.yaml` with PostgreSQL and MinIO.
  **Container-platform matrix is separate** from the native matrix: `linux/amd64` and `linux/arm64`
  only, on chiseled ASP.NET base images.
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
3. **RID matrix (Phase 10)** — three tiers: verified (`linux-x64`, `linux-arm64`, `win-x64`,
   `osx-arm64`), built-but-unverified (`win-arm64`, `osx-x64`, musl variants), and not published.
   Containers are a separate matrix: `linux/amd64` and `linux/arm64`.
4. **Retention (Phase 11)** — the defaults table in Phase 11 applies, covering records, evidence,
   audit, logs, exports, backups, and caches, with the backup-lag residual risk stated explicitly.
5. **Work-item source of truth (Phase 6)** — **DevBuddy owns the work item.** GitHub is imported
   read-only as linked references and snapshots, never written back, so `SyncSources` is one-way.

## Remaining open items

None blocking. Anything discovered during a phase is raised before that phase's exit criteria are
claimed, and recorded as an ADR rather than decided silently in code.
