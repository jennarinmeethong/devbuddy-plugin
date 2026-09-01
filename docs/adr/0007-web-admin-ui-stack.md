# ADR-0007: React admin UI, no web chat

- Status: Accepted
- Date: 2026-09-01
- Phase: 8

## Context

`info.md` requires a web administration UI in the first release using React, TypeScript, Tailwind
CSS, and shadcn/ui, covering login, account recovery, workspace/team/project and membership
administration, project AI-access policy, draft review, edit and approval, published-record
history, audit visibility appropriate to the role, and basic system health. It states explicitly
that AI questions remain in Claude and Codex, and that a separate web chat is not in the initial
scope.

## Decision

Vite, React, TypeScript, Tailwind, and shadcn/ui in `web/admin`, with TanStack Query for server
state and React Router for navigation. The API client is generated from the OpenAPI document the
API produces at build time, so a contract change breaks the build rather than the browser.

The approval screen shows the exact revision under review together with its content hash, and
submits that hash. The UI cannot approve the latest revision implicitly.

Navigation is role-driven: the UI hides what the server would refuse, and the server refuses
regardless of what the UI shows.

No chat interface, no AI question box, no model calls from the browser.

## Consequences

- The approval binding required by SB-23 is expressed in the interface a human actually uses, not
  only in the domain, so a race between reading and approving cannot slip through.
- A generated client means the UI cannot drift silently from the API.
- Cost: a Node toolchain enters the repository and the release pipeline. Scoped to `web/admin`.

## Alternatives considered

- **Server-rendered UI in .NET.** One fewer toolchain, but `info.md` specifies this stack.
- **Adding a web chat.** Excluded by `info.md`, and it would create a second AI entry point that
  the MCP allow-list does not cover.
