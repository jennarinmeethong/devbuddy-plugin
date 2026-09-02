# devbuddy-plugin

An engineering-knowledge system that retains work identity, context, decisions, technical
knowledge, change impact, and handover notes, so that whoever picks up a piece of work next can ask
informed questions and continue safely.

MCP-first: one shared .NET core is exposed through a console app, a web API, and an MCP server,
with thin Claude and Codex plugin packages over the same tool surface. Self-hosted, PostgreSQL as
the source of truth, AI access denied by default and enabled per project.

## Status

**Phases 0 to 6 complete of 11.** 41 use cases
behind one pipeline, PostgreSQL with full-text search, MinIO evidence storage, the product own
sign-in with lockout and rotating tokens, tenant isolation enforced server-side on every request, a
draft-to-published path whose audit history records who approved exactly which revision, read-only
analysis that provably executes nothing, and a secret scanner that refuses credentials on the way in
and redacts them on the way out. There is no MCP server and no web UI yet.

**Seventeen of 33 security controls are verified; eight more are implemented but unproven.**
Source synchronisation reads a mounted working copy rather than the GitHub API, so pull requests
and issues are not available. Do not connect real project data until the remaining controls are
verified.

## Documents

| Document | What it is |
|---|---|
| [info.md](info.md) | Decisions confirmed by the project owner. Binding. |
| [docs/plan.md](docs/plan.md) | The phased implementation plan, Phase 0 to Phase 11, with exit criteria. |
| [docs/security/threat-model.md](docs/security/threat-model.md) | Assets, trust boundaries, adversaries, threats. |
| [docs/security/security-baseline.md](docs/security/security-baseline.md) | The 33 controls and how each will be proved. |
| [docs/security/verification-matrix.md](docs/security/verification-matrix.md) | What has actually been tested. Currently: nothing. |
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
