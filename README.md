# devbuddy-plugin

An engineering-knowledge system that retains work identity, context, decisions, technical
knowledge, change impact, and handover notes, so that whoever picks up a piece of work next can ask
informed questions and continue safely.

MCP-first: one shared .NET core is exposed through a console app, a web API, and an MCP server,
with thin Claude and Codex plugin packages over the same tool surface. Self-hosted, PostgreSQL as
the source of truth, AI access denied by default and enabled per project.

## Status

**Phases 0 to 2 complete of 11.** The domain model and the application layer exist and are
tested: tenancy, access, work identity, the knowledge record lifecycle, and 40 use cases behind a
single pipeline that enforces validation, identity, authorization, redaction, and audit. Every
port is still an interface: there is no database, object store, authentication, MCP server, or web
UI. **No security control has been verified end to end**; ten are partially implemented. Do not
connect real project data.

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
