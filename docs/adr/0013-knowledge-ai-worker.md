# ADR-0013: The knowledge AI worker, and what authorises it

- Status: **Accepted** 2026-09-10 (`info.md`) — the two permitted shapes are binding.
  Embedding generation as a worker feature additionally waits on ADR-0012's provider gate.
- Date: 2026-09-10
- Phase: 12C

## Context

`info.md` under AI Usage: "Add a separate `knowledge-ai-worker` only when autonomous background
work is required, such as scheduled source analysis, embedding generation, stale-record detection,
or recurring reports. An AI worker must use a configured LLM or embedding API, and must account for
authentication, cost, permissions, source provenance, and human approval before important output is
published."

The features are the easy half. The design problem is the one this system has already answered
twice, in the same direction both times:

- **`restore_system` was deleted rather than fixed.** Every operation is authorised against a
  membership, and a restore from total loss runs against a database with no memberships in it, so
  the operation could never have succeeded. It became a console command, and a test asserts no
  restore operation exists.
- **`retention` was kept out of the pipeline.** A sweep spans every workspace and project and has
  no caller to authorise it against. Phase 12B gave it a schedule and pointedly did not give it a
  caller.

A background worker is the third instance, and it is the hard one, because unlike those two it
wants to touch things a person's permissions gate: records, drafts, source repositories, project
content.

## Decision proposed

**Two shapes are permitted. Nothing between them is.**

**Shape A — the worker is a caller.** It holds a machine token bound to a user and a workspace,
exactly like any plugin credential since 2026-09-10, and it is bounded by that membership through
the ordinary pipeline. Every existing control applies unchanged: `AuthorizationService` refuses a
request naming another workspace before it looks the caller up, per-project AI policy decides what
it may read, SB-17 refuses a secret, SB-18 redacts on the AI channel, and every call is audited
naming it as the actor.

Consequences that follow and are features rather than costs: the worker appears in audit history
as itself, its token is revocable on the next call from the `Machine tokens` screen, and it can be
given a viewer-or-contributor membership so that "autonomous" never means "privileged". A worker
that needs to see two workspaces holds two tokens and runs twice, which is the same answer the
plugins got.

**Shape B — the worker is not a caller.** It runs outside the pipeline like `retention`, and it may
therefore touch **nothing a person's permissions would gate**. Installation-wide maintenance only:
sweeps, index rebuilds, counting things. It reads no record content into an output anybody sees, it
creates no draft, and it publishes nothing.

**Anything between the two is refused.** A worker that runs outside the pipeline and reads project
content is the installation-wide superuser this system has deliberately never had, running
unattended, which is worse than one a person has to invoke. There is no "service account with
elevated scope" in this design and this ADR is the place that says so.

**Human approval before publication is unchanged and not negotiable.** `info.md` requires it, and
the lifecycle already enforces it: `create_draft` is AI-exposed, `approve_record` and
`publish_record` are human-only, and an approval is bound to the exact content revision reviewed.
A worker in shape A can create a draft. It cannot approve its own, and neither can anybody through
it — the same check applies to it as to a person, which is why the draft creator being allowed to
approve their own draft is not a loophole here: the worker is not a person and has no reviewer
permission unless somebody granted its membership one, which is a decision visible in the `Teams`
and membership screens.

**Cost and scheduling.** A worker's schedule belongs beside the retention service in
`docker/compose.yaml`, off unless configured, with the same posture as telemetry, SMTP, and the
GitHub API mode. An LLM or embedding API means per-call spend on a loop nobody is watching, so a
bounded budget and a refusal when it is exhausted are part of this decision, not an operational
afterthought. Embedding generation additionally depends on ADR-0012, which is itself unapproved.

## Amendment — 2026-09-10: the channel follows the model, not the worker

Found by writing the first job, which is the only way this kind of thing is ever found.

The decision above says a caller-bound worker is "bounded by it exactly like any other caller", and
the first reading of that put every worker on `AccessChannel.Ai` — the strictest channel, and the
only one where the per-project AI access policy and the SB-18 personal-data redaction apply. That
reasoning was sound and the conclusion was too broad.

**The AI channel is also an allow-list.** Every feature this ADR proposes —
scheduled source analysis, stale-record detection, embedding generation — needs `ManageIndex` or
`ManageSources`, and both are `AiExposure.Denied`. A worker pinned to the AI channel can search,
read, analyse and create a draft, and nothing else. It cannot do the work it was proposed for. The
two halves of this ADR contradicted each other and nobody noticed until there was a job to run.

**The resolution: the channel follows whether the job sends content to a model.**

- A job that sends content to an LLM or an embedding provider runs on `AccessChannel.Ai`, because
  that is where the controls protecting that act live.
- A job that touches no model runs on `AccessChannel.InternalSystem` — still holding a real
  machine token, still bounded by a live membership, a role, and the credential's workspace
  ceiling. What it is not subject to are AI-specific rules about a model it never calls.

`AccessChannel.InternalSystem` existed since Phase 2 and had never been used by product code. It
is worth stating why it is safe to use now: `AuthorizationService` does **not** special-case it.
An `InternalSystem` caller needs an existing account, an enabled account, a live membership
covering the scope, and a role carrying the permission, exactly like a human. The channel decides
which AI-specific rules apply on top, and nothing else.

Two things keep this from becoming a loophole:

- **The declaration is on the job type**, not on a call, so it cannot vary per run.
- **A job that declared no model use and then reaches for a model is refused.** `EmbeddingGateway`
  takes a caller and rejects one that is not on the AI channel. Declaring no model use buys a job
  the human-only operations its membership carries; it does not also buy it the model.

## Consequences

- Stale-record detection, recurring handover reports, and scheduled source analysis become possible
  without a person triggering each one, which is the gap between a knowledge system somebody
  maintains and one that maintains itself.
- Shape A means the worker's reach is a membership somebody granted and can revoke, and is visible
  in the same screens as everybody else's. That is the strongest property on offer here.
- Shape A also means a real credential exists in a deployment, unattended. It is scoped, revocable,
  and audited, and it is still a credential, so the workspace-layout rules about where settings live
  apply to it.
- Two shapes is more design than one, and the boundary between them will be argued about. That is
  preferable to the argument being had per feature.
- Nothing here weakens the AI allow-list. A worker calls the same use cases as anybody else; the
  MCP tool surface is unchanged.

## Alternatives considered

- **No worker.** The status quo, and what happens if this is not approved. Everything remains
  triggered by a person or by the retention schedule. Costs nothing and leaves stale-record
  detection and recurring reports undone.
- **One privileged service identity for the installation.** Simplest to build and it is the exact
  thing this system removed when it deleted `restore_system` and when it refused an
  installation-wide administrator role for `create_workspace`. Rejected.
- **A worker that runs inside the pipeline with a synthetic caller minted per job.** Rejected: a
  caller nobody granted is a superuser with extra steps, and the audit row would name an actor no
  person is accountable for.
- **Run the worker as whoever owns the project.** Tempting, and it makes the worker act as somebody
  who did not ask it to. Their audit history would then contain actions they never took, which
  destroys the provenance the whole system exists to preserve. Rejected.
