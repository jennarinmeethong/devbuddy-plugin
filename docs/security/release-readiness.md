# Release Readiness

Phase 11 exit criteria, from `docs/plan.md`: the verification matrix has no silent gaps, and every
remaining risk is named and accepted explicitly by the project owner before real project data is
connected. This note is that naming. `docs/security/verification-matrix.md` is the per-control
source of truth; this note does not repeat its 34 rows, only the ones whose status moved last and
the eight Phase 11 scenarios.

## Status

**All 34 controls are `TESTED`. 0 are `IMPLEMENTED`, 0 are `NOT IMPLEMENTED`.**

Thirty-three reached that state by `v1.0.0`. The thirty-fourth, SB-34, is the embedding egress path
ADR-0012 required a control for; it was added and reached `TESTED` on 2026-09-13, and has a section
of its own below.

## SB-29 — closed by the first published release

`v1.0.0` is published: run 34045844221, built from `9a8ebf0`. Three images in GHCR, each with its
own CycloneDX SBOM, every archive signed with GitHub's keyless OIDC identity, and the whole of it
verified **after publication and from outside the workflow that made it** — provenance naming this
repository, `.github/workflows/release.yml`, `refs/tags/v1.0.0` and `9a8ebf0`; the `linux-x64`
archive matching its attested digest byte for byte and matching `SHA256SUMS`; a deliberately wrong
`--owner` refused, so that a pass means something.

That was the last of the original rows: **all 33 controls were `TESTED`** at `v1.0.0`. The
thirty-fourth, SB-34, came with Phase 12C.

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

## SB-34 — embedding egress, added and closed 2026-09-13

Phase 12C opened a path out of the boundary v1 did not have, and ADR-0012 required it to be a
control of its own rather than read as covered by SB-17 and SB-18. SB-34 is that control.
`EmbeddingEgressTests` runs the real `record-embedding-sweep` job and the real
`search_similar_records` operation over PostgreSQL with pgvector, through the shipped pipeline, and
replaces only the HTTP handler at the far end, so its assertions are about what the provider
actually received rather than about what a job reported: a project nobody opened to AI contributes
nothing, a secret never reaches the provider, drafts are never sent, and a query carrying a secret
is refused with nothing sent.

It found a defect before it proved anything. The sweep read a field the history operation does not
produce, so on a real installation it would have reported success having embedded nothing; its unit
test had passed throughout because the fake answered in the same wrong shape.

What it does not accept is a hosted provider. That mode is enabled nowhere, and switching it on
needs the vendor named, an `OutboundAccess:AllowedHosts` entry and an acceptance of its own beside
the 2026-09-10 one. The self-hosted mode has run end to end once, on the owner's test installation
with synthetic data only, and no provider or worker has run against real project data.

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

## Before real project data was connected — accepted 2026-09-10

Per `info.md`, this was the explicit sign-off point, and it is signed off. The entry is
*Confirmed Phase 12 Approval and the Release-Readiness Acceptance — 2026-09-10* in `info.md`, and
this section records what it accepted and what changed rather than what was asked for. Until that
entry existed the gate was open in fact and closed only on paper, which is the one state this
project set out not to be in.

**1. Application logs — accepted, and narrower than it was.** Three of the four things this
section used to hand to the operator are now decided or closed.

| What it was | Where it stands |
| --- | --- |
| Whether anything runs `retention` on a schedule | **Closed.** `docker/compose.yaml` runs `retention --every 24h` in the console image, and `DeploymentTests` fails if that service or its interval goes. The scheduler is a loop around the same sweep the one-shot command runs; `RetentionScheduleTests` asserts that rather than assuming it. Verified in the shipped stack from clean, not only in the file: the service came up with its first pass logged. |
| Which option from `docs/operations/logging.md` is in force | **Decided: option 2.** Logs go to the aggregator in `docker/compose.observability.yaml` and Loki's ninety days is the retention of record. The application's file sink stays on as the local copy with its own sweep, so the window is enforced twice. |
| Whether `Logging:File:Path` stays set | **Still the operator's**, and it now costs less: clearing it drops the local copy, not the time-based window, because Loki holds that. |
| That setup and recovery tokens are written to those logs by design | **Closed.** `Email:AllowTokensInLog` is false, so nothing writes a token to a log unless an operator sets that on purpose. Verified against the running stack in both directions: a real recovery request logged that the message could not be delivered and no token, and the same request with the opt-in set wrote the token to stdout and to the file on the volume. |

Who can read them: whoever can reach Grafana, which is published on loopback only and sits behind
the same reverse proxy as the API.

The last row is the substantive change of the two, and it is worth being exact about what it did
and did not do. It did not add a control over who may read a log file. It removed the reason there
was a credential in one: the fallback sender now records that a message could not be delivered, to
whom, and how to fix that, and writes the token nowhere. Anybody who sets
`Email:AllowTokensInLog=true` is accepting the original risk knowingly, which is the difference
between a decision and a default. Refusing to start outside development without a delivery channel
was the other candidate and was rejected — it closes the same risk and breaks a plain `docker run`
for a first-time operator.

Consequences worth stating, since a closed risk that quietly breaks a workflow is not closed:

- **Inviting somebody still works with no mail server.** `create_user_account` returns the setup
  token in its own response, which was always a real delivery path rather than a fallback one.
- **Self-service password recovery does not.** With no SMTP and no opt-in, a recovery token is
  generated, is unreachable, and expires. That is deliberate. An administrator's alternative is to
  configure SMTP, or to set the opt-in, read the token, and turn it off again. **Since Phase 13
  (D1) there is a third way:** `issue_password_reset` hands the administrator a single-use reset
  token in its own response, and never writes it to a log or to the audit trail. It is refused
  unless the administrator holds `ManageAccounts` in every workspace the person belongs to.
  Without that rule, resetting a shared member's password would be a way into a workspace the
  administrator does not run.
- **Log export to Loki is now on by default with the overlay**, which was previously held false
  precisely because tokens were in those logs. An operator who turns the token opt-in on should
  turn log export back off.

**2. The unverified platforms — accepted, and they keep shipping.** Four when accepted, two today;
the narrowing is below. `osx-arm64`, `osx-x64`, `win-arm64` and `linux-musl-arm64` were built and
published for `v1.0.0` without ever being started, because no macOS and no Windows on ARM was
available here. The owner's decision is to keep
publishing them in the built-but-unverified tier with every release's notes saying so verbatim,
rather than to acquire the hardware or to stop publishing. Whoever deploys on one of them is the
first to run it, and nobody may describe them as supported.

**Narrowed on 2026-09-13: three platforms, not four.** The owner removed `osx-x64` from the
platforms this project supports, so it is no longer built or published. `osx-arm64`, `win-arm64`
and `linux-musl-arm64` stay on the terms above. Archives earlier releases published for `osx-x64`
remain as they were released, unverified.

**Narrowed again the same day: two platforms.** `osx-arm64` moved to the verified tier after it was
run natively on an Apple M4 Mac mini, so it is smoke-tested before every release rather than
accepted as unrun. `win-arm64` and `linux-musl-arm64` remain on the terms above.

**Run once, and still unverified: 2026-09-14.** Both remaining RIDs were started for the first time,
against the `v1.2.0` draft's archives. `win-arm64` ran in a VMware VM on Apple silicon, and
`linux-musl-arm64` in an Alpine container on the Apple M4. Both passed the smoke test. The owner
kept them in the built-but-unverified tier (`info.md`, 2026-09-14). **On 2026-09-21 `linux-musl-arm64`
moved to the verified tier** (Phase 13, B10), after runs for four releases; `win-arm64` alone remains
here. The acceptance above stands,
with one change of wording: release notes now say what was run for these two, not that they were
never started. Nobody may describe them as supported.

**Closed rather than accepted: `linux/arm64` container images.** This section, ADR-0008 and the
release matrix all said they were not built and not claimed. They are built now, published under
the same tag as the amd64 images, and all three were started and answered. Cross-compiled rather
than emulated, so it costs a release minutes; started under emulation rather than on hardware,
which is the same standard the native `linux-arm64` row already held and is recorded that way.

**Re-verified for `v1.1.0`, by hand:** the destroy-and-restore drill, against a harder disaster
than the one `backup-and-restore.md` describes — both the database *and* the evidence volumes
destroyed, so the row that catches "rows came back and bytes did not" was actually exercised rather
than trivially satisfied. All six rows passed; the artefacts came back byte for byte from the
backup, the approval was still bound to the same content hash, and the machine token minted before
the disaster still resolved. `docs/operations/release-matrix.md` records it. It also found one
overstatement in the documentation rather than in the code: an access token issued before a restore
still validates afterwards, because it is a stateless JWT inside its lifetime, so "sessions are not
restored" covers refreshing and not tokens already issued. **Closed in code by Phase 13 (D6):**
every access token now names its session, and both web hosts refuse one whose session has no live
refresh token. That covers a restore, a sign-out, and a revoke-all.

**Still the operator's, and narrowed in Phase 13:** the residual lag before deleted data ages out of a
backup, and the fact that a permission revoked today does not retrieve a copy somebody downloaded
yesterday. For the first: a deleted project no longer comes back when an older backup is restored,
because a deletion ledger beside the backups is re-applied by `restore` (D7). What remains is a
backup copied off the volume and restored after the ledger was pruned (`backup-and-restore.md`). Both are in `info.md` under Accepted Security Limitations.

Nothing here moves a control's status. All 33 of that date remain `TESTED`, and SB-14, SB-15,
SB-27 and SB-29 gain evidence: `EmailSenderTests` is four cases over the token opt-in and its default,
`RetentionScheduleTests` twenty-seven over the sweep and the loop around it — including that both
entry points call the identical delegate, so "the same sweep" is asserted rather than assumed —
and `DeploymentTests` seven more over the shipped file, covering the scheduler, the email default,
the cross-compilation flags in all three Dockerfiles, and both architectures in the release
workflow.
