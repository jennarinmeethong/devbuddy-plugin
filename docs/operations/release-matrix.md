# Release matrix

What is built, what has actually been run, and what is not claimed at all.

The point of this file is that the third column is honest. A matrix listing eight platforms as
"supported" because eight `dotnet publish` commands exited zero would be a list of guesses, and the
one somebody deploys to would be the one nobody tried.

## Self-contained executables

Published with `--self-contained true`, per runtime identifier. **Self-contained does not mean
single-file and does not mean Native AOT**; neither is claimed and neither is built.

| RID | Built | Run | How it was run |
|---|---|---|---|
| `win-x64` | yes | **yes** | Natively, on the machine this was developed on. |
| `linux-x64` | yes | **yes** | `ubuntu:24.04`, in a container. |
| `linux-arm64` | yes | **yes** | `ubuntu:24.04` under `linux/arm64` emulation. Emulated, not hardware. |
| `linux-musl-x64` | yes | **yes** | `alpine:3`, after installing the dependencies below. |
| `osx-arm64` | yes | no | No macOS available. Published as-is. |
| `osx-x64` | yes | no | No macOS available. Published as-is. |
| `win-arm64` | yes | no | No Windows on ARM available. Published as-is. |
| `linux-musl-arm64` | yes | no | Not run; the x64 musl build was, so the dependency list is believed to carry over. |

The four rows reading **no** are a decision rather than an omission, confirmed by the project owner
on 2026-09-10: keep publishing them in this tier, with every release's notes saying they were never
started, rather than acquire the hardware or stop publishing. No macOS and no Windows on ARM is
available here. Whoever deploys on one of them is the first to run it, and nobody may describe them
as supported. The alternative — dropping them — stays available and costs nothing but four rows.

"Run" means the executable started and answered — `DevBuddy.Cli operations --ai` returned the
eighteen AI-exposed operation names. It does not mean the full test suite ran on that platform.
The suite runs on `ubuntu-latest` and `windows-latest` in CI; everything else in this table is a
smoke test or a build.

Anything not listed is **not published and not supported**. That is not a gap to fill on request:
each row is a thing somebody has to keep working.

## Native dependencies

Self-contained ships the .NET runtime. It does not ship the operating system's C library, its C++
runtime, or ICU, and this is where "self-contained" misleads people.

| Platform | Needs | If it is missing |
|---|---|---|
| Linux (glibc) | `libicu` | Fails at startup: *"Couldn't find a valid ICU package installed on the system."* Observed on a bare `ubuntu:24.04`. |
| Linux (musl) | `libstdc++`, `libgcc`, `icu-libs` | Fails at startup with missing shared libraries and unresolved symbols. Observed on a bare `alpine:3`. |
| Windows | nothing beyond the OS | — |
| macOS | nothing beyond the OS | Not verified. |

Setting `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` removes the ICU requirement and removes
culture-aware behaviour with it. It is a legitimate choice for a container that only ever speaks
one language, and it is a choice — not a default, and not something to set to make an error go
away.

## Container images

A separate matrix, deliberately. A container image and a native executable fail in different ways
and are verified differently.

| Image | Base | Platforms | Verified |
|---|---|---|---|
| `devbuddy-api` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | `linux/amd64`, `linux/arm64` | Built and run on both: healthy in the Compose stack serving sign-in and operations on amd64; on arm64, `/health` 200 and `/operations` 401 unauthenticated. |
| `devbuddy-mcp` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | `linux/amd64`, `linux/arm64` | Built and run on both: refuses an unauthenticated call with 401. |
| `devbuddy-migrate` (console) | `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` | `linux/amd64`, `linux/arm64` | Built and run on both: applied migrations, bootstrapped, and performed a restore on amd64; on arm64, applied all six migrations and listed the eighteen AI-exposed operations. |

**`linux/arm64` is built and started, as of 2026-09-10.** This table said "not built and not
claimed" through v1, and ADR-0008 and the 2026-09-07 amendment to `info.md` said the same; the
build was a CI change nobody had made, so the row would have been a guess. Phase 12B made it.

Two things about how, because both matter to what the row means.

**Cross-compiled, not emulated.** The SDK stage is pinned to the builder's own architecture with
`--platform=$BUILDPLATFORM` and `-a $TARGETARCH` decides what comes out, so an arm64 image is
compiled at native speed on an amd64 runner. Nothing in these Dockerfiles RUNs on the target
platform — the runtime stage is a file copy — so the whole compiler never goes through QEMU. The
alternative works and costs a release hours, which is how a platform ends up quietly deleted.

**Started under emulation, not on hardware.** No arm64 hardware is available to this project, and
that is stated rather than blurred: this is the same standard the `linux-arm64` native RID row
already held. It does mean nobody has run these images on a Graviton or an Apple-silicon host.

The release workflow's non-root check runs **per architecture**, because a manifest list can hold
one image that drops root and one that does not, and a `docker pull` without `--platform` would
only ever check the runner's own.

Chiseled means no shell, no package manager, and no busybox: if something gets code execution in
one of these containers, there is nothing in it to execute. It also means the container health
check cannot be `curl`, which is why the API implements `--health-check` as a mode of itself.

### One known noise

Npgsql probes for Kerberos support at startup and the chiseled image has no `libgssapi_krb5.so.2`,
so every container logs:

```
Cannot load library libgssapi_krb5.so.2
Error: libgssapi_krb5.so.2: cannot open shared object file: No such file or directory
```

It is harmless — the connection proceeds, migrations apply, everything works — and it is recorded
here because it reads like a failure and is not. Adding the library would mean a larger base image
for no functional gain.

## Cutting a release

`.github/workflows/release.yml` runs on a `v*` tag. It gates on the full suite, the format check,
and the web build before it publishes anything; then it builds every RID in the first table, pushes
the three images to GHCR for `linux/amd64` and `linux/arm64` under one tag each, generates one SBOM
per host, and signs all of it with GitHub's keyless OIDC identity — provenance for the archives and the images, and each host's SBOM attached to its
own image.

It leaves the GitHub release as a **draft**. That is deliberate: the checklist below asks for smoke
tests on platforms no runner has and a drill performed by hand, and a workflow that published
itself would be claiming those happened.

```bash
git tag v1.0.0
git push origin v1.0.0
```

## What was verified for v1.0.0

Recorded because the checklist below asks for it per release, and because a table that says "yes"
without saying when is the guess this file exists to avoid.

Built from `9a8ebf0`. Tag `v1.0.0`, run 34045844221, **published 2026-09-06**.

Two drafts of this version were withdrawn before that one, and both are described rather than
quietly replaced. The first was pulled because using the system found three defects that reading it
had not: a symlink escape past the path guard that only Linux exposed, a password beginning with
`@` parsed as a response file — which broke the first command any new installation runs — and an
evidence store with a read side and no write side, so the drill below could not be performed by
anybody. The second was pulled for something no binary could show: its source archive carried the
Compose file from before the MinIO key, so an operator deploying from the tag rather than from
`main` would have met the same 500 the drill had just found. The binaries in it were sound. A
release is not only its binaries.

Two columns, because they mean different things. **Re-run** was performed against this tag's own
artefacts. **Carried over** was performed against `6a60a48` and is not claimed as repeated; that is
honest here only because the commits between `6a60a48` and `9a8ebf0` changed documentation, one
code comment, `docker/compose.yaml`, `docker/.env.example` and two test files, and no product code.
It would not be honest for a release that changed any.

**That licence is now spent.** The project owner confirmed on 2026-09-10 that every release
re-runs its own checklist in full. Phase 12 changed product code — the console, the fallback email
sender, all three Dockerfiles and the Compose stack — so the next tag has nothing left to carry,
and a release that carries everything is not a verified release.

| Check | When | Result |
| --- | --- | --- |
| Attestations verify from outside the workflow | Re-run, after publication | **Yes.** All three images and the `linux-x64` archive. Provenance names this repository, `.github/workflows/release.yml`, `refs/tags/v1.0.0`, and source commit `9a8ebf0`; the archive was downloaded from the published release and its SHA-256 matches both the attested digest and `SHA256SUMS`. Checked against a negative control — a deliberately wrong `--owner` is refused — so a passing check means something. |
| SBOM attached per image | Re-run | **Yes.** CycloneDX, attested under predicate `https://cyclonedx.org/bom`, and distinct per host: 36, 38 and 56 components for the API, the MCP server and the console. That they differ is the point of one document per image rather than one per archive. |
| `win-x64` smoke test | Re-run | **Yes.** Natively on the development machine, from the published archive. Eighteen AI-exposed operations, exit 0. |
| `linux-x64` smoke test | Re-run | **Yes.** `ubuntu:24.04` with `libicu74`, `uname -m` reporting `x86_64`. Eighteen operations, exit 0. |
| The new operations stay off the AI surface | Re-run | **Yes**, in the shipped binaries rather than only in tests: neither `capture_evidence` nor `publish_record` appears in `operations --ai` on either platform smoke-tested. |
| `linux-arm64` smoke test | Carried over | **Yes**, against `6a60a48`. `ubuntu:24.04` under `linux/arm64` emulation, `uname -m` reporting `aarch64`. Emulated, not hardware. |
| `linux-musl-x64` smoke test | Carried over | **Yes**, against `6a60a48`. `alpine:3` with `libstdc++`, `libgcc` and `icu-libs`. |
| Compose from clean to healthy | Carried over | **Yes**, against `6a60a48` plus the MinIO key this tag carries. All services up, `migrate` exited 0, `/health` 200, and `/operations`, the evidence upload route and the MCP transport all 401 unauthenticated — against a control showing an unmapped path answers 404, so those 401s mean the routes exist rather than that everything is refused. Neither 5432 nor 9000 is reachable from the host (SB-30, confirmed against the running stack rather than against the file). The log volume came back owned by the application user with a rolled file in it. |
| Destroy-and-restore drill | Carried over | **Yes, by hand, 2026-09-06.** A published record with an approval bound to its content hash, two attached artefacts, twelve audit entries, an account and a machine token; backed up, the backup copied off the volume, the database volume destroyed, migrated empty, restored, and every one of the six rows in `backup-and-restore.md` checked. The artefact came back **byte for byte** — the same SHA-256 as the file that went in, not merely a row saying bytes exist. Sessions were not restored, as designed. Restoring a second time was refused with "This installation already has data". |
| `osx-arm64`, `osx-x64`, `win-arm64`, `linux-musl-arm64` | — | **Not run, for either tag.** Built and published as-is. No macOS and no Windows on ARM available; the musl arm64 build was not run, and the x64 musl one that was is the only reason its dependency list is believed to carry over. The release notes say this verbatim. |

SB-29 is **`TESTED`**: a published release carries the SBOMs and the attestations, and they were
verified from outside the workflow after publication rather than on the strength of a signing step
exiting zero.

### What the drill found

The evidence upload failed with a 500 against the shipped stack:
`Server side encryption specified but KMS is not configured`.

`EvidenceStoreOptions.UseServerSideEncryption` defaults to true, so the application asks for AES256
on every object, and MinIO refuses that write outright when it has no key. `docker/compose.yaml`
ran MinIO without one. Nothing had noticed because until `capture_evidence` shipped there was no
way to upload evidence at all, and the unit tests turn encryption off for their own container —
with a comment claiming it "stays on by default for a real deployment", which was true of the
default and false of the deployment.

Fixed by giving the bundled MinIO a key (`MINIO_KMS_SECRET_KEY`, from `docker/.env`) rather than by
turning encryption off, so artefacts are still encrypted at rest. `DeploymentTests` now checks the
compose file for it, and the misleading comment is gone. The drill was then re-run from an empty
stack and completed.

## What was verified for Phase 12 (2026-09-10, not a release)

Recorded here rather than waiting for the next tag, because these are the checks the tag will
otherwise be tempted to carry over. Performed against `main`, not against a published artefact.

| Check | Result |
| --- | --- |
| All three images build for `linux/arm64` | **Yes.** Cross-compiled on an amd64 host with `--platform=$BUILDPLATFORM` and `-a $TARGETARCH`; `docker image inspect` reports `arm64` and user `1654` for each. |
| `devbuddy-migrate` starts on arm64 | **Yes.** Applied all six migrations against `postgres:17-alpine` on arm64, then listed the eighteen AI-exposed operations. |
| `devbuddy-api` starts on arm64 | **Yes.** `/health` 200; `/operations` 401 unauthenticated, so the route exists rather than everything being refused. `dotnet --info` inside the container reports `arm64`. |
| `devbuddy-mcp` starts on arm64 | **Yes.** `POST /` 401 unauthenticated, against a control showing an unmapped path answers 404. |
| The scheduled retention sweep runs in a container | **Yes.** `retention --every 1m` in the console image against real PostgreSQL: two passes a minute apart, both reporting all six counts. |
| It stops when the container is stopped | **Yes.** `docker stop` returned in under a second with exit code 0 and the schedule's own closing line in the log — SIGTERM handled, not a ten-second kill. |
| The `retention` service runs in the shipped stack | **Yes.** `docker compose up -d` from clean on `linux/amd64`: all five services up, `api` healthy, `migrate` exited 0, and `retention` up with its first pass logged and all six counts reported. The log volume came back owned by the application user with a rolled file in it. |
| `/health`, `/operations` and the MCP transport | **Yes.** 200, 401, and 401 respectively, with the UI served at the root; an unmatched path answers 200 because that is the client-route fallback, which `AdminUiTests` pins against shadowing an endpoint. |
| Tokens stay out of the log by default | **Yes**, against the running stack and not only in tests. `POST /auth/recovery/begin` for a real account answered 202 and the API logged that the message could not be delivered, naming the address, the subject, and both ways to fix it — and no token, in stdout or in the file on the volume. |
| The opt-in still works when it is asked for | **Yes.** With `DEVBUDDY_EMAIL_ALLOW_TOKENS_IN_LOG=true` the same request wrote the token to the log and to the file on the volume, under a line saying to treat it as sensitive. Both halves matter: the default suppresses it, and it is not a broken sender. |

Emulated, not hardware, for every arm64 row above. The Compose stack and the token checks were run
on `linux/amd64`. Not claimed: the Compose stack from clean on arm64, and the destroy-and-restore
drill on either architecture for this change — that one is a release check and Phase 12 is not a
release.

## Verifying a release

Before publishing the draft, and recorded per release. Nothing is carried over from a previous
tag: that licence was spent on `v1.0.0` and is closed by the 2026-09-10 decision above.

1. `dotnet test DevBuddy.slnx -c Release` on Linux, with Docker available, so the integration and
   drill tests actually run. (The workflow does this too; doing it locally is what lets you read
   the failures.)
2. `dotnet publish` for every row in the first table. A row that fails to build comes out of the
   table; it does not get a footnote.
3. Run the smoke test on every row whose "Run" column says yes. If a platform cannot be run this
   time, its "Run" column changes to no for that release.
4. `docker compose -f docker/compose.yaml up -d` from clean, to healthy — including the
   `retention` service, which should log a pass on start and stay up.
5. Start each image on **both** published architectures and record what answered. An arm64 image
   that was built and never started belongs in a "built" column, not a "verified" one.
6. The destroy-and-restore drill in `docs/operations/backup-and-restore.md`, by hand, once.
7. SBOM and vulnerability scan attached to the release (CI produces both).
8. The signatures verify from outside the workflow that made them:

   ```bash
   gh attestation verify oci://ghcr.io/<owner>/<repo>/devbuddy-api:1.0.0 --owner <owner>
   gh attestation verify devbuddy-linux-x64.tar.gz --owner <owner>
   ```

   A release whose attestations do not verify is not a release; that is the whole point of SB-29,
   and checking it here is what stops "signed" from meaning "a signing step exited zero".

Release notes state, verbatim, which rows were unverified.
