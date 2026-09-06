# Release Readiness

Phase 11 exit criteria, from `docs/plan.md`: the verification matrix has no silent gaps, and every
remaining risk is named and accepted explicitly by the project owner before real project data is
connected. This note is that naming. `docs/security/verification-matrix.md` is the per-control
source of truth; this note does not repeat its 33 rows, only the ones whose status moved last and
the eight Phase 11 scenarios.

## Status

**All 33 controls are `TESTED`. 0 are `IMPLEMENTED`, 0 are `NOT IMPLEMENTED`.**

## SB-29 — closed by the first published release

`v1.0.0` is published: run 34045844221, built from `9a8ebf0`. Three images in GHCR, each with its
own CycloneDX SBOM, every archive signed with GitHub's keyless OIDC identity, and the whole of it
verified **after publication and from outside the workflow that made it** — provenance naming this
repository, `.github/workflows/release.yml`, `refs/tags/v1.0.0` and `9a8ebf0`; the `linux-x64`
archive matching its attested digest byte for byte and matching `SHA256SUMS`; a deliberately wrong
`--owner` refused, so that a pass means something.

That is the last row. **All 33 controls are `TESTED`.**

An earlier draft of this same version was withdrawn rather than published, and the reason is worth
keeping: its source archive carried the Compose file from before the MinIO key, so an operator
deploying from the tag rather than from `main` would have met the same 500 the restore drill had
just found. The binaries were fine. A release is not only its binaries.

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

**Closed since:** application log retention is now application code with a test against it. The
hosts write a daily file when `Logging:File:Path` is set — `docker/compose.yaml` sets it — and
`dotnet run -- retention` deletes files past `Logging:File:RetentionDays`, the same sweep that
already handled audit events, evidence, backups, and exports. That is the last of the ten rows of
the Phase 11 schedule to move from "operator responsibility" to a passing test.

What remains is a choice rather than a gap, and it stays on the list below for that reason: an
operator can clear the path (reverting to the size-bounded log driver), point logs at the
observability overlay's Loki instead, or run nothing on a schedule to perform the sweep. A window
nothing sweeps is a window in name only. `docs/operations/logging.md` sets out the three options
and what each costs.

## Added since Phase 11: a telemetry egress, and the rule that bounds it

Worth naming here rather than only in the operations docs, because it is a new path by which data
can leave the boundary — the first one this system has that is not the audit store or an API
response.

`Telemetry:Endpoint` is empty by default and nothing is registered when it is, so an installation
that wants none of this exports nothing and pays nothing. When an operator does configure it, what
leaves is bounded by a rule stated in `DevBuddyTelemetry` and enforced by six tests: a tag may
carry an operation name, an outcome, a channel, or a scanner rule name — a fixed vocabulary this
codebase defines — and never a workspace, project, record, or user identifier, a denial reason, or
anything a caller supplied.

Three consequences follow from that rule rather than from oversight: database instrumentation is
absent (statement text exceeds the vocabulary even parameterised), URL path and query are stripped
from every span while the route template survives, and log export is a separate opt-in that stays
false while `EmailOptions.Provider` is `Log`.

This does not change any control's status. It was verified against the running stack as well as in
tests: with a workspace, project, and actor created and traffic driven through Succeeded, Denied,
and Blocked, no exported series or span carried any of those identifiers, and a blocked secret
appeared only as its rule's name.

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

1. Application log retention is now enforced and tested — the shipped stack writes daily files
   and `dotnet run -- retention` deletes them past ninety days — but three things about it are the
   operator's, and none of them is visible from inside the code. Whether anything actually runs
   `retention` on a schedule; whether the path stays set, since clearing it reverts to the
   size-bounded log driver; and, the one that matters most, that **with no SMTP configured, setup
   and recovery tokens are written to those logs by design**. A retention window is not a control
   over who can read the file while it exists. Record which option from
   `docs/operations/logging.md` is in force and who can read the volume.
2. `v1.0.0` is published, signed, and carries an SBOM per image, and SB-29 is `TESTED` on the
   strength of a verification performed after publication rather than of a workflow having
   succeeded. What is left is what the release notes state verbatim and this acceptance should
   name too: four platforms — `osx-arm64`, `osx-x64`, `win-arm64` and `linux-musl-arm64` — were
   built and published **without ever being run**, because no macOS and no Windows on ARM is
   available here, and `linux/arm64` container images are not built at all. If somebody deploys on
   one of those, they are the first to run it. The rest of the release checklist was carried over
   from `6a60a48` rather than re-run, which is sound only because the commits between it and
   `9a8ebf0` changed no product code — a claim that stops being true for the next release.

Neither blocks the controls that are `TESTED` today, including the personal-data policy (SB-18),
retention (SB-27), and the AI-channel and tenant-isolation controls that gate access to whatever
data a workspace holds.
