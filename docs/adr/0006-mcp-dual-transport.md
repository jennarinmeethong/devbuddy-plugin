# ADR-0006: MCP ships stdio and authenticated HTTP, one allow-list

- Status: Accepted
- Date: 2026-09-01
- Phase: 7

## Context

`info.md` requires that only safe, scoped operations reach AI through MCP: search, get, analyse,
create a draft, and generate a handover. Everything else stays under human or internal-system
control. Local plugins launch a server as a child process; a self-hosted deployment needs a remote
surface. The project owner confirmed on 2026-09-01 that both transports ship in the first release.

## Decision

`DevBuddy.McpServer` ships both transports in v1:

- **stdio** for locally launched Claude and Codex plugins. Identity comes from a machine-scoped
  token in the plugin configuration.
- **authenticated HTTP** for self-hosted and remote use, sharing the same bearer tokens as the API.

Both transports resolve to the same tool allow-list and the same Application authorization
pipeline. The tool-surface equality test runs once per transport, so a transport cannot widen the
surface. Anything outside the allow-list is absent from the tool list, not merely refused.

## Consequences

- One place defines what AI can do, which is what makes control SB-07 checkable at all.
- Two transports means two authentication paths to review. Bounded, because both terminate in the
  same pipeline.
- A machine-scoped stdio token is a credential on a developer machine. It is scoped to the projects
  that user can already reach, so it grants no new access, and it is revocable.

## Alternatives considered

- **stdio only.** Cannot serve a self-hosted team deployment.
- **HTTP only.** Adds a network hop and a running server for a purely local workflow.
- **Different tool sets per transport.** Rejected outright: it makes the AI surface a moving target.
