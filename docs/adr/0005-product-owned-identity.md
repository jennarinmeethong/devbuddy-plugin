# ADR-0005: Product-owned identity, no external identity provider

- Status: Accepted
- Date: 2026-09-01
- Phase: 4

## Context

`info.md` requires the product to use its own login and account system initially, and explicitly
excludes organisation login, enterprise SSO, and external identity providers from the initial
version. It also requires multi-workspace, team, project, repository, and environment scoping in
the data model from the first version, and roles including viewer, contributor, reviewer, and
administrator. Choosing GitHub as the Git host does not change this.

## Decision

ASP.NET Core Identity for accounts and password storage. Short-lived JWT access tokens plus
rotating, revocable refresh tokens. Lockout on repeated failures and rate limiting on the
authentication endpoints. Account recovery uses a single-use, time-boxed token and returns an
identical response whether or not the account exists.

Authorization is a step in the Application pipeline, not a concern of any host. A caller-supplied
workspace or project identifier is never trusted; membership and resource permission are re-checked
server-side on every request. EF Core global query filters sit behind that as defence in depth,
never as a replacement for it.

## Consequences

- No dependency on an external identity provider being reachable, which matters for a self-hosted
  deployment.
- The project owns password storage, lockout, recovery, and token revocation, and therefore owns
  the risk. Controls SB-13 to SB-16 exist for exactly this reason.
- Adding SSO later means adding an authentication scheme, not redesigning authorization, because
  authorization already depends on membership rather than on how the user signed in.

## Alternatives considered

- **OIDC against an external provider from the start.** Less credential risk to own, but excluded
  by `info.md` and unavailable in an air-gapped self-hosted deployment.
- **Long-lived API tokens only.** Simpler, but no revocation story worth the name. Rejected.
