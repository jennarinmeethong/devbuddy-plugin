# Release Readiness

Phase 11 exit criteria, from `docs/plan.md`: the verification matrix has no silent gaps, and every
remaining risk is named and accepted explicitly by the project owner before real project data is
connected. This note is that naming. `docs/security/verification-matrix.md` is the per-control
source of truth; this note does not repeat its 33 rows, only the one that is not `TESTED` and the
eight Phase 11 scenarios.

## Status

**32 of 33 controls are `TESTED`. 1 is `IMPLEMENTED` but not fully proven, by name below. 0 are
`NOT IMPLEMENTED`.**

## The one control not at `TESTED`

### SB-29 — SBOM and verifiable artifact origin

Unchanged from Phase 10: SBOMs are generated per host and uploaded as build artifacts, but no
release has carried one, and nothing is signed or attested. Moves to `TESTED` when a release
actually ships one.

## SB-27 — closed since Phase 11

At the end of Phase 11, SB-27 (retention applies to all data copies) carried three residual items:
exports had no stored artefact to purge, a deleted project had no purge trigger, and application
log retention sat outside this codebase. The first two are closed:

- **Exports now write an actual copy.** `export_project` writes records, work items, and evidence
  bytes to disk (`ExportService`) instead of only returning a manifest, so the 30-day export
  expiry the schedule always named now has something to act on. `dotnet run -- retention` purges
  an export directory past its window the same way it purges a stale backup.
- **A deleted project is purged immediately.** `delete_project` (Administrator, workspace- or
  project-scoped) removes a project's work items, records with their full revision history,
  evidence rows and bytes, source repositories, and project-scoped memberships in one call. The
  schedule's "hard purge within 30 days" is satisfied trivially by an immediate delete; there is
  no lagging state for a sweep to catch, so the sweep this control once lacked a trigger for is
  unneeded rather than missing. Audit history survives the project it describes.

**Still residual, and still not a defect:** application log retention is a container log-driver
setting (`docker/compose.yaml` bounds it by size), not application code a test can assert a
90-day window against. It is an operator responsibility, named here so it stays visible rather
than silently dropped; `docs/operations/logging.md` is the runbook — how to measure the real
volume, the three options with their trade-offs, and the two things that are in the logs
regardless of how long they are kept.

## Also closed since Phase 11, none of them a security-control change

These close functional v1 gaps CLAUDE.md named — they add human-gated capabilities with their own
tests, and none of them widens the AI-exposed surface or changes an existing control's status:

- **Team administration** (`create_team`, `rename_team`, `delete_team`, `list_teams`,
  `list_team_members`, `add_team_member`, `remove_team_member`) — the entity and its table existed
  since Phase 1 with nothing reading or writing them. A team still carries no permission of its
  own; `Membership` decides what anyone may do, unchanged.
- **A second workspace.** `create_workspace` is sponsored by a workspace the caller already
  administers, authorised through the ordinary pipeline against that sponsor workspace — not a new
  installation-wide role. `IInstallationBootstrapper` is untouched: it still makes the first
  workspace once, on an empty database, and still refuses afterwards.
- **Screens for all three**, so `info.md`'s "workspace/team/project and membership administration"
  is met from a browser and not only over HTTP: a `Teams` screen, a `Workspaces` screen, and
  project deletion behind typing the project's name back. Both new navigation entries are gated on
  the permission, and the viewer-navigation test asserts neither is offered without it. The gating
  is a courtesy, never a control — the server refuses regardless, which is what the .NET tests
  prove.
- **Setup and recovery token delivery.** `IEmailSender` (`EmailOptions.Provider`: `Log` by default,
  `Smtp` via MailKit when configured) replaces the ad hoc "hand it to whoever is looking" paths.
  The `Log` fallback fixes a real bug found while building this: the previous code logged a
  warning claiming a recovery token was "written to this log" without the token actually being in
  it.
- **GitHub API source synchronisation**, opt-in via `GitHubOptions.Mode = GitHubApi`, behind the
  same `ISourceSystemClient` port the working-copy reader already used. Pull requests, issues, and
  review comments are now readable when configured; a mounted working copy still cannot answer
  those, and says so rather than returning an empty list. Requires both a token and an entry in
  `OutboundAccess:AllowedHosts` — the empty-allow-list default (SB-03) is unchanged by this
  existing.

## Phase 11 scenario coverage

| # | Scenario | Status |
|---|---|---|
| 1 | Cross-user, team, and project access, incl. search, caches, attachments, exports | Exercised |
| 2 | Revoked permissions take effect immediately | Exercised |
| 3 | Malicious source and tool content produces no unauthorized action | Exercised |
| 4 | Sensitive-data leakage: secrets and personal data blocked before retention and egress | Exercised |
| 5 | Approval revision mismatch is rejected | Exercised |
| 6 | Resource exhaustion limits | Exercised |
| 7 | Backup, restore, and incident response | Exercised |
| 8 | Retention and deletion across copies | Exercised |

All eight scenarios are now fully exercised.

## Before real project data is connected

Per `info.md`, this is the explicit sign-off point. The project owner should accept, in writing:

1. Application log retention is an operator responsibility (log-driver or aggregator
   configuration), not a tested application behaviour. Pick an option from
   `docs/operations/logging.md` and record which — and note that with no SMTP configured, setup
   and recovery tokens are written to those logs by design.
2. No release has yet been signed or carries an attached SBOM (SB-29). `.github/workflows/release.yml`
   builds, pushes, signs, and attests everything on a `v*` tag and leaves the release as a draft
   for a person to publish; until a tag is actually cut, this row stays `IMPLEMENTED`.

Neither blocks the controls that are `TESTED` today, including the personal-data policy (SB-18),
retention (SB-27), and the AI-channel and tenant-isolation controls that gate access to whatever
data a workspace holds.
