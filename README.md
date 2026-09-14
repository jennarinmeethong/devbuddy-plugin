# devbuddy-plugin

An engineering-knowledge system that retains work identity, context, decisions, technical
knowledge, change impact, and handover notes, so that whoever picks up a piece of work next can ask
informed questions and continue safely.

MCP-first: one shared .NET core is exposed through a console app, a web API, and an MCP server,
with thin Claude and Codex plugin packages over the same tool surface. Self-hosted, PostgreSQL as
the source of truth, AI access denied by default and enabled per project.

## Status

**v1 is released, and Phase 12 followed it.** `v1.0.0` (2026-09-06), `v1.1.0` (2026-09-10) and
`v1.2.0` (2026-09-14, the current release) are published, signed, with an SBOM per image. 58
operations behind one pipeline plus two streaming evidence routes, PostgreSQL with full-text search, MinIO evidence
storage, the product's own sign-in with lockout and rotating tokens, tenant isolation enforced
server-side on every request, a draft-to-published path whose audit history records who approved
exactly which revision, read-only analysis that provably executes nothing, a secret scanner that
refuses credentials on the way in and redacts them on the way out, and three hosts over that one
core — an HTTP API, an MCP server on stdio and authenticated HTTP, and a console — a React
administration UI served by the API, covering workspaces, teams and project deletion, thin Claude
and Codex plugin packages over that same MCP server, and a self-hosted Compose stack that schedules
its own retention sweep, with images for `linux/amd64` and `linux/arm64`.

**All 34 security controls are `TESTED`.** Source synchronisation reads a mounted working copy by
default and the GitHub API when configured. Embeddings, semantic search and the background worker
exist and are off by default; no hosted embedding provider is enabled anywhere, and no provider or
worker has run against real project data. The owner's acceptance for connecting real project data
is in `docs/security/release-readiness.md`.

**Deploy from `v1.2.0` or later, not from the `v1.0.0` or `v1.1.0` tag's Compose file.** Both of
those name `minio/minio` on Docker Hub, which no longer serves it, so the evidence store cannot
start without a cached image. `v1.2.0` is the first release that takes it from `quay.io`, pinned by
digest.

## Documents

| Document | What it is |
|---|---|
| [คู่มือภาษาไทย — DevBuddy Handbook](docs/manual/devbuddy-guide.th.html) | Detailed offline HTML manual: cream claymorphism theme, installation, all MCP tools, administration, operations, and development. |
| [info.md](info.md) | Decisions confirmed by the project owner. Binding. |
| [docs/plan.md](docs/plan.md) | The phased implementation plan, Phase 0 to Phase 12, with exit criteria. |
| [docs/security/threat-model.md](docs/security/threat-model.md) | Assets, trust boundaries, adversaries, threats. |
| [docs/security/security-baseline.md](docs/security/security-baseline.md) | The 34 controls and how each is proved. |
| [docs/security/verification-matrix.md](docs/security/verification-matrix.md) | What has actually been tested. The only place a control counts as proven. |
| [docs/operations/plugin-hosts.md](docs/operations/plugin-hosts.md) | Installing the plugins, and what the tool boundary does not cover. |
| [docs/operations/workspace-layout.md](docs/operations/workspace-layout.md) | Arranging checkouts on a machine that works against more than one deployment. |
| [docs/operations/deployment.md](docs/operations/deployment.md) | Running the stack, its secrets, and its limits. |
| [docs/operations/backup-and-restore.md](docs/operations/backup-and-restore.md) | What a backup holds, and the drill. |
| [docs/operations/observability.md](docs/operations/observability.md) | The optional traces/metrics/logs overlay, and what it refuses to collect. |
| [docs/operations/release-matrix.md](docs/operations/release-matrix.md) | Which platforms were built, and which were actually run. |
| [docs/adr/](docs/adr/) | Architecture decision records. |
| [AGENTS.md](AGENTS.md) | Contributor guide: structure, commands, style, testing, security expectations. |

## Build

Requires the .NET 10 SDK (pinned in `global.json`).

```bash
dotnet build DevBuddy.slnx -c Release
```

```bash
dotnet test DevBuddy.slnx -c Release
```

The web client is built and tested with [Bun](https://bun.com):

```bash
cd web/admin && bun install && bun run build && bun test
```
