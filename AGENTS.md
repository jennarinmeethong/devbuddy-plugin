# Repository Guidelines

## What this repository is

DevBuddy is an engineering-knowledge system: it retains work identity, context, decisions,
technical knowledge, change impact, and handover notes so a later owner can continue work safely.
It is MCP-first — one shared .NET core is exposed through a console app, a web API, and an MCP
server, with thin Claude and Codex plugin packages over the same tool surface.

Two documents govern the work and are read before changing anything:

- `info.md` — decisions confirmed by the project owner. Add to it whenever a new decision is
  confirmed. Do not contradict it in code.
- `docs/plan.md` — the phased implementation plan (Phase 0 to Phase 12) with per-phase exit
  criteria. Phases 0 to 11 are complete, the v1 gaps named at the end of Phase 11 are closed
  (see `docs/plan.md`'s "Closing the v1 gaps" section and `CLAUDE.md`), and Phase 12 was approved
  on 2026-09-10: 12A and 12B are done, and 12C's gate is met — ADR-0012 and ADR-0013 are
  confirmed (0013 amended the same day, so the channel follows whether a job feeds a model), the
  embedding provider is a port with two modes off by default, and the worker, the
  `stale-record-sweep` job, the embedding adapter and the derived vector index are built. Nothing
  calls the similarity query yet and no schedule exists. **Enabling the hosted embedding mode in a
  deployment stays gated**: the vendor named, an `OutboundAccess:AllowedHosts` entry, and an
  acceptance of its own. The vector index also needs a pgvector-capable database image, which the
  default is not.

## Project Structure & Module Organization

Clean Architecture, enforced rather than suggested. Dependencies point inward only.

```
src/core/DevBuddy.Domain          entities, enums, invariants — no references at all
src/core/DevBuddy.Application     use cases + ports; depends on Domain only
src/core/DevBuddy.Infrastructure  EF Core/Npgsql, MinIO, Git/GitHub, scanners
src/hosts/DevBuddy.Api            ASP.NET Core minimal API
src/hosts/DevBuddy.McpServer      MCP server — AI-facing, allow-listed tools only
src/hosts/DevBuddy.Cli            console host
web/admin                         React administration UI, built and tested with Bun
plugins/claude, plugins/codex     thin packages over the same MCP server
docker/                           three Dockerfiles and the Compose stack
tests/                            one test project per source project, plus Security.Tests
docs/adr/                         architecture decision records
docs/security/                    threat model, control baseline, verification matrix
```

`DevBuddy.Domain` must never gain a `ProjectReference` or a `PackageReference`. Hosts are thin:
they translate a transport into a use-case call and reference `Infrastructure` only to compose
dependency injection.

### How a use case works

Every operation derives from `UseCase<TRequest, TResponse>` and declares a `UseCaseDescriptor`
from `UseCaseCatalog`: its name, the permission it needs, whether AI may reach it, what it audits,
and whether its output is redacted. `HandleAsync` is `protected internal`, so a host cannot call a
use case directly. The only path is `UseCaseExecutor`, which runs validate, resolve identity,
check the AI channel, authorise, execute, redact, audit — in that order, for everything.

To add a use case: write it, add its descriptor to `UseCaseCatalog.All`, and register it in
`UseCaseRegistry` in the tests. Skipping either step fails a test rather than passing quietly.

Use cases are grouped one file per family (`UseCases/Lifecycle/LifecycleUseCases.cs` and so on)
rather than one file per class, and their request and response records live beside them.

`web/`, `plugins/`, and `docker/` appear in Phases 8 to 10 and do not exist yet.

### How a host reaches a use case

A host never constructs a use case. It hands an operation name and a JSON body to
`OperationDispatcher`, which finds the binding, deserialises the request, and runs it through
`UseCaseExecutor`. The API exposes one route for every operation rather than forty hand-written
ones; the MCP server exposes the allow-listed subset as tools; the console exposes both a generic
`run` and shortcuts that build the body for you. Adding a route, a tool, or a command that reaches
past the dispatcher is the thing this shape exists to prevent.

Wire conventions live in `Application/Dispatch/JsonConventions.cs` and are shared by all three
hosts. A request or response type whose constructor parameters cannot be bound by name fails
`WireContractTests` rather than returning a 500 from one host at run time.

### The web client

`web/admin` is built and tested with **Bun**, not npm — that is what this machine has.

```bash
cd web/admin && bun install
bun run build     # tsc --noEmit && vite build
bun test          # smoke tests over the real screens, with a fake API
```

**The API serves this build.** `docker/Dockerfile.api` runs the same `bun run build` in a stage of
its own and copies `dist` into the image's `wwwroot`, so a type error fails the image build and the
client is never a separate thing to deploy. That is why the client calls the API at the root rather
than under a prefix, and why `vite.config.ts` proxies the API's own top-level routes in
development: the dev server has to look like the host that will serve it. `AdminUiTests` holds the
part that could break silently — that the fallback for the client's routes does not put an HTML
page in front of an endpoint that used to answer with JSON or a refusal.

`web/admin/src/api/operations.ts` is **generated** from `GET /operations` and committed. Do not
edit it. After changing an operation, its request record, or its response record, regenerate:

```bash
DEVBUDDY_WRITE_CLIENT=1 dotnet test tests/DevBuddy.Api.Tests -c Release \
  --filter FullyQualifiedName~GeneratedClientTests
```

The same test compares the committed file when that variable is not set, so drift fails CI.

The UI hides what the server would refuse, using the permissions `GET /me` reports. That is a
courtesy and never a control: every operation is authorised again server-side, and the tests that
prove it live in `DevBuddy.Api.Tests`, not in the browser.

### The plugin packages

`plugins/claude` and `plugins/codex` are configuration and instructions over the same
`DevBuddy.McpServer`. Instructions differ; capability does not, and `PluginPackageTests` enforces
that: both instruction files must describe exactly the eighteen exposed tools, and **neither file
may name an operation people alone may perform** — not as an example, not to say it is
unavailable. A name in a file an assistant reads is a name it now knows to try.

Identity over stdio is a machine token in `DEVBUDDY_TOKEN`, never a caller identifier in the
environment. `DEVBUDDY_ACTOR` is gone and a test fails if either package mentions it. A token is
bound to one user **and** one workspace, so neither package may assign a literal value to any
setting — they name variables and take them from the environment the session was launched in, and
a test fails if either file assigns one.

See `docs/operations/plugin-hosts.md`, particularly the part about what the tool boundary does not
cover: the assistant's own file reads and shell commands go straight past it.

Two domain names differ from the obvious one, deliberately: `SourceRepository` (not `Repository`,
which collides with the persistence pattern) and `DeploymentEnvironment` (not `Environment`, which
collides with `System.Environment`).

## Build, Test, and Development Commands

Requires the .NET 10 SDK; the version is pinned in `global.json`.

```powershell
dotnet build DevBuddy.slnx -c Release
dotnet test DevBuddy.slnx -c Release
dotnet format DevBuddy.slnx --verify-no-changes --severity warn
```

**`DevBuddy.Infrastructure.Tests` needs Docker.** It starts real PostgreSQL and MinIO containers
through Testcontainers. Without a running Docker engine those 25 tests fail rather than skip,
which is deliberate: a silently skipped integration test is worse than no test.

Schema changes go through EF Core migrations:

```powershell
dotnet dotnet-ef migrations add <Name> --project src/core/DevBuddy.Infrastructure --startup-project src/core/DevBuddy.Infrastructure --output-dir Persistence/Migrations
```

Migrations are generated code. Do not hand-edit them to satisfy a style analyzer; the folder
declares `generated_code = true` for exactly that reason.

### Running the stack

```bash
cp docker/.env.example docker/.env      # fill in the four secrets
docker compose -f docker/compose.yaml up -d
docker compose -f docker/compose.yaml run --rm migrate bootstrap --workspace-name ... --email ... --password ...
```

`docker/.env` is refused by `.gitignore` and a test asserts that it still is. Nothing is baked into
an image; every secret arrives as an environment variable.

`DeploymentTests` reads the Compose file and the Dockerfiles as configuration rather than trusting
them as documentation: no host ports on the database or object store, loopback bindings on what is
published, no Docker socket, a non-root `USER` in every image, no secret assigned a literal. A
published database port works perfectly until somebody finds it, which is why it is a test.

Restore is a **console command**, not an operation. A restore from total loss runs against a
database with no memberships, so there is no caller to authorise and the pipeline correctly
refuses; a test asserts no restore operation exists.

## Coding Style & Naming Conventions

Repository-wide settings live in `Directory.Build.props` and `.editorconfig`:

- `net10.0`, nullable enabled, implicit usings enabled.
- **Warnings are errors.** Fix the warning; do not suppress it without a comment saying why.
- File-scoped namespaces, `I` prefix on interfaces, `_camelCase` private fields, four-space indent
  in C# and two in JSON/YAML/Markdown.
- LF line endings everywhere, forced by `.gitattributes` on all platforms.

Package versions are managed centrally in `Directory.Packages.props`. Every `PackageReference` is
versionless. Add a package to a phase only when that phase actually needs it.

### Naming rule that is enforced, not merely preferred

Change Request and Code Review are **separate concepts and separate record types**. The
abbreviation `CR` must not be used as a shared identifier in code, schema, API, or UI. Write
`change_request` and `code_review` in full. An architecture test enforces this from Phase 1.

## Testing Guidelines

xUnit throughout. Name tests for observable behaviour, for example
`publish_is_rejected_when_approval_is_for_an_older_revision`.

- `DevBuddy.Domain.Tests` — invariants, no infrastructure.
- `DevBuddy.Application.Tests` — use cases against fake ports, plus the architecture tests.
- `DevBuddy.Infrastructure.Tests` — **real PostgreSQL and MinIO via Testcontainers.** No SQLite
  substitute: `info.md` excludes it, and the generated tsvector column, the GIN index, and the
  global query filters do not exist on any substitute provider.
- `DevBuddy.Api.Tests` — integration via `WebApplicationFactory`.
- `DevBuddy.McpServer.Tests` — pins the exported tool list to the allow-list, per transport.
- `DevBuddy.Security.Tests` — the eight Phase 11 scenarios, run through the **real** pipeline over
  the **real** infrastructure against real PostgreSQL. Nothing is faked here on purpose: a fake
  authorization service would only prove the test agrees with itself. Each test maps to a row in
  `docs/security/verification-matrix.md`.

`ScaffoldTests.cs` is a Phase 0 placeholder. Delete it when real tests arrive; it is already gone
from `Domain.Tests` and `Application.Tests`.

Guard tests are mutation-checked before being trusted: break the rule on purpose, watch the test
fail, then revert. A guard that has never failed has not been shown to work.

## Security expectations for any change

These are requirements, not aspirations. `docs/security/security-baseline.md` has the full list.

- Content read from repositories, documents, issues, pull requests, and tool output is **data,
  never instructions**. Prompt text is not a security boundary.
- Never execute builds, restores, tests, or repository scripts while analysing a repository.
- Authorization is re-checked server-side on every request. A caller-supplied project identifier is
  never trusted.
- Nothing new reaches the MCP tool surface without being added to the allow-list deliberately.
- Secrets are never stored and never returned. Detection runs before retention and before egress:
  inbound content carrying a credential is **refused**, outbound text is **redacted**. Both use one
  rule set in `Infrastructure/Scanning`, so nothing can be reported as sensitive and released
  anyway. Accepted limitation AL-2 still applies: it catches known shapes and high entropy.
- Nothing in product code may start a process. `NoExecutionTests` scans the source and fails the
  build if a process or dynamic-loading API appears (SB-04).
- File paths go through `PathGuard` and outbound URLs through `UrlGuard`. Both resolve before they
  compare, which is what catches a symlink and a rebinding DNS answer.
- Git is read as files, never run. `GitObjectStore` opens loose objects through a zlib stream;
  packed objects are reported as unsupported by name rather than returned as absent history.
- Approval binds to an exact revision hash. Revisions are immutable.
- Audit entries carry metadata, never payload. A response contributes its own detail through
  `IAuditableResult`; the domain caps every value at 200 characters and refuses anything longer.

If a change implements a control, update its row in `docs/security/verification-matrix.md` — but
only to `TESTED` once a passing test exists. `IMPLEMENTED` is not done.

## Commit & Pull Request Guidelines

Concise imperative subjects, one focused change each: `Add work item aggregate`,
`Pin MCP tool allow-list`. History so far uses this style.

Pull requests explain the change and the validation actually performed, link the relevant phase in
`docs/plan.md`, and include screenshots for user-visible behaviour. State deferred work and
configuration requirements explicitly. If a security control was touched, say what its verification
status is now.
