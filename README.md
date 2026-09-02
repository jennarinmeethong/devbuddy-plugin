# devbuddy-plugin

An engineering-knowledge system that retains work identity, context, decisions, technical
knowledge, change impact, and handover notes, so that whoever picks up a piece of work next can ask
informed questions and continue safely.

MCP-first: one shared .NET core is exposed through a console app, a web API, and an MCP server,
with thin Claude and Codex plugin packages over the same tool surface. Self-hosted, PostgreSQL as
the source of truth, AI access denied by default and enabled per project.

## Status

**Phases 0 to 5 complete of 11.** The domain model, the application layer, persistence, identity,
and the knowledge lifecycle exist and are tested: 41 use cases behind one pipeline, PostgreSQL with
full-text search, MinIO evidence storage, the product own sign-in with lockout and rotating tokens,
tenant isolation enforced server-side on every request, and a draft-to-published path whose audit
history records who approved exactly which revision. There is no MCP server, no secret scanner, and
no web UI yet.

**Nine of 33 security controls are verified; ten more are implemented but unproven, and the
redactor that ships today redacts nothing.** Do not connect real project data.

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
