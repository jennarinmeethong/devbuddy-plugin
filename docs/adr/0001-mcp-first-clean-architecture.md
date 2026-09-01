# ADR-0001: MCP-first architecture with one shared core

- Status: Accepted
- Date: 2026-09-01
- Phase: 0-2, 7

## Context

`info.md` requires an MCP-first architecture: one shared knowledge core with a console app, a web
API, and an MCP server, plus separate Claude and Codex plugin packages that do not duplicate core
logic. It also requires SOLID principles and Clean Architecture, with domain and application logic
independent of UI, transport, persistence, and AI-provider implementations.

## Decision

Four layers, one direction of dependency:

- `DevBuddy.Domain` — entities, value objects, enums, invariants. No project or package references.
- `DevBuddy.Application` — use cases and ports (interfaces). Depends on Domain only.
- `DevBuddy.Infrastructure` — adapters implementing the ports.
- Hosts (`Api`, `McpServer`, `Cli`) — thin. They translate a transport into a use case call and
  reference Infrastructure only to compose dependency injection.

The rule is enforced by an architecture test, not by convention.

## Consequences

- A capability is written once and reaches all three hosts.
- The MCP surface becomes a deliberate allow-list over existing use cases rather than a parallel
  implementation, which is what makes ADR-0006 and control SB-07 possible.
- Cross-cutting concerns (validation, authorization, redaction, audit) live in one pipeline in the
  Application layer, so no host can skip them.
- Cost: more projects and more indirection than a single-assembly design. Accepted, because the
  alternative puts security-relevant logic in three places.

## Alternatives considered

- **Single assembly with folders.** Faster to start; nothing stops a host from reaching past the
  use-case pipeline and skipping authorization. Rejected on that ground alone.
- **MCP server as a wrapper over the HTTP API.** Simpler, but makes the MCP surface depend on the
  full API surface being safe to expose, which inverts the allow-list guarantee. Rejected.
