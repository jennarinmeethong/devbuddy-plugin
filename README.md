# devbuddy-plugin

An engineering-knowledge system that retains work identity, context, decisions, technical
knowledge, change impact, and handover notes, so that whoever picks up a piece of work next can ask
informed questions and continue safely.

MCP-first: one shared .NET core is exposed through a console app, a web API, and an MCP server,
with thin Claude and Codex plugin packages over the same tool surface. Self-hosted, PostgreSQL as
the source of truth, AI access denied by default and enabled per project.

## Status

**Phase 0 of 11 complete — scaffolding and governance only.** The solution builds and the
placeholder tests pass. There is no domain logic, persistence, authentication, MCP tooling, or web
UI yet, and **no security control has been implemented or verified**. Do not connect real project
data.

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
