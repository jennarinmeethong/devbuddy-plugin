# ADR-0006: MCP ships stdio and authenticated HTTP, one allow-list

- Status: Accepted; amended 2026-09-27 (Phase 14, A4), below
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
- **authenticated HTTP** for self-hosted and remote use. Until 2026-09-27 it took the same bearer
  tokens as the API; since then it takes a machine token, the credential stdio takes, and nothing
  else (amendment below).

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

## Amendment — 2026-09-27 (Phase 14, A4)

The owner decided that the plugins reach the server over HTTPS through the gateway, and that the
HTTP transport takes a machine token instead of the API's access token (`info.md`, same day).

- **Why the access token did not fit.** It lasts fifteen minutes and no assistant can refresh it,
  and it carries no workspace, so it reached every workspace its owner belonged to. A machine token
  is minted by its owner, bound to one workspace, and revocable on the next call.
- **What changed.** `MachineTokenAuthenticationHandler` resolves the bearer against the store on
  every request, the MCP transport being stateless, and the workspace travels into the caller
  context as the same ceiling stdio applies. An access token is refused like any unknown bearer.
  The transport is mapped at `/mcp`, so the gateway can pass that path to it and everything else to
  the API. A token in the server's own environment is never read for an HTTP request.
- **Rate limit.** Per person, at the API's defaults and settings (`RateLimiting:RequestPermitLimit`,
  600 a minute). A request with no working token is not counted: behind the gateway every caller
  shares one address, so counting those would let anybody without a token lock out everybody with
  one, and a token cannot be guessed.
- **Consequence.** Two authentication paths remain, but they now take the same credential and
  resolve it through the same service. `HttpTransportTests` holds the HTTP half.
