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
| `osx-arm64` | yes | **yes** | Natively, on an Apple M4 Mac mini running macOS 26.6.2. Hardware, not emulation. First run 2026-09-13, outside a release; see below. |
| `win-arm64` | yes | once | **Not in the verified tier.** Started once, for `v1.2.0` on 2026-09-14, in a VMware guest on Apple silicon. Not smoke-tested per release. |
| `linux-musl-arm64` | yes | once | **Not in the verified tier.** Started once, for `v1.2.0` on 2026-09-14, in `alpine:3` on an Apple M4. Not smoke-tested per release. |

The two rows reading **once** are a decision rather than an omission. They are published in this
tier without the per-release smoke test the rows reading yes get, as confirmed by the project owner
on 2026-09-10. Until 2026-09-14 neither had ever been started. That day both ran for `v1.2.0`:
`win-arm64` in a VMware virtual machine on Apple silicon, and `linux-musl-arm64` in an Alpine
container on the Mac mini. The owner confirmed that they stay in this tier (`info.md`, 2026-09-14).
One run in a VM or a container is not a per-release commitment. Every release's notes say plainly
what was and was not run for these two, and nobody may describe them as supported.

**`osx-arm64` moved to the verified tier (2026-09-13).** The owner moved it after a Mac mini became
available and the build was run on it. From the next release on it is smoke-tested by hand on that
machine like every other row reading yes, and a release for which it cannot be run records its
"Run" column as no, per step 3 of the checklist below.

**`osx-x64` is no longer published (2026-09-13).** It was a fourth row in this tier until the owner
removed it from the platforms this project supports. It had never been run here. The release
workflow no longer builds it, so it now falls under *not published and not supported* below.
Archives that releases tagged before that date published for it are left where they are.

"Run" means the executable started and answered — `DevBuddy.Cli operations --ai` returned the
AI-exposed operation names — eighteen when the rows below were recorded, twenty since semantic
search and the embedding sweep landed, and the check is that the list is the catalogue's rather
than that it is any particular length. It does not mean the full test suite ran on that platform.
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
| macOS | nothing beyond the OS | Started on macOS 26.6.2 on 2026-09-13, on a machine that also has other software installed, so not a clean-OS check. |

Setting `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` removes the ICU requirement and removes
culture-aware behaviour with it. It is a legitimate choice for a container that only ever speaks
one language, and it is a choice — not a default, and not something to set to make an error go
away.

## Container images

A separate matrix, deliberately. A container image and a native executable fail in different ways
and are verified differently.

| Image | Base | Platforms | Verified |
|---|---|---|---|
| `devbuddy-api` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | `linux/amd64`, `linux/arm64` | Built and run on both: healthy in the Compose stack serving sign-in and operations on amd64; on arm64, `/health` 200, `/operations` 401 unauthenticated, and the UI 200 at the root. |
| `devbuddy-mcp` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | `linux/amd64`, `linux/arm64` | Built and run on both: refuses an unauthenticated call with 401, against a control showing an unmapped path answers 404. |
| `devbuddy-migrate` (console) | `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` | `linux/amd64`, `linux/arm64` | Built and run on both: applied migrations, bootstrapped, and performed a restore on amd64; on arm64, applied all six migrations and listed the eighteen AI-exposed operations. |

Both architectures were checked against the **published** `1.1.0` images pulled by digest, not only
against a local build. First shipped in `v1.1.0`.

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

## Verifying the release workflow itself, without cutting a release

Every action in `.github/workflows/release.yml` was bumped a major version after `v1.1.0` (the
Node 20 deprecation), and four of them are reachable **only** from that workflow, so CI could not
touch them: `download-artifact`, `attest-build-provenance`, `attest`, and `action-gh-release`.
Three are on the SB-29 path. Reading their `action.yml` at the target tag confirms the inputs still
exist; it does not confirm the workflow runs.

**The way to confirm it is a throwaway prerelease tag.** `v1.1.1-rc.1` from `23216fd`, run
34474906604, 2026-09-10. All 14 jobs passed, the Node 20 warning was gone, and the four
release-only actions worked: eight archives and the SBOMs collected, three images pushed and
attested, a draft created with 12 assets. Attestations verified from outside the workflow for all
three images, including the SBOM predicate at `https://cyclonedx.org/bom` with the same 36
components as `v1.1.0`'s API image.

It also found two things reading could not, which is the entire argument for doing it this way.

| Found | What it was |
| --- | --- |
| `actions/attest-sbom` is deprecated | It had been invisible at `@v1`; bumping to `@v4` surfaced the notice. Migrated to `actions/attest@v4`, which takes `sbom-path` directly — a rename, and `predicate-type` must not be set alongside it. |
| A prerelease draft was not marked as one | `action-gh-release` was given `draft: true` and never `prerelease`, so the `v1.1.1-rc.1` draft carried `prerelease=false`. Publishing it would have made a release candidate the repository's Latest release ahead of `v1.1.0`. Now derived from the hyphen in the tag. |

And one thing it confirmed rather than found: **the moving container tag was not hijacked.**
`docker/metadata-action` skips `{{major}}.{{minor}}` for a prerelease, so `1.1` still pointed at
`v1.1.0`'s digest (`sha256:05645a37…`) after the rc run, while the rc got its own
(`sha256:39f3122e…`). Had it moved, anybody pinned to `1.1` would have been served a release
candidate without asking for one.

Do this before the next real tag whenever `release.yml` changes. The rc tag and its draft are
deleted afterwards. The GHCR image versions it pushes need the `delete:packages` scope, which is a
separate grant. Until 2026-09-14 they were left. Since then every rc image version has been deleted
at the owner's instruction, and each deletion followed the same guard. First, the rc index digest is
confirmed not to be referenced by, and to share no child manifest with, any released tag. Next, the
version is re-read immediately before deletion, to confirm its digest and that it carries no release
tag. Finally, the released tags are checked to resolve to the same digests, and the latest release's
attestations to still verify.

**Both fixes were then proved the same way.** `v1.1.1-rc.2` from `3c78cb8`, run 34477446655,
2026-09-10. All 14 jobs passed and the run carried **no annotations at all** — the `attest-sbom`
deprecation notice is gone, and so is the Node 20 one.

| Claim | Result |
| --- | --- |
| `actions/attest@v4` still produces a verifiable CycloneDX attestation | **Yes**, on all three images, verified from outside the workflow: predicate `https://cyclonedx.org/bom` with 36, 38 and 56 components for the API, the MCP server and the console — the same figures `attest-sbom` produced, so the migration changed the action and not the artefact. |
| Provenance is unaffected | **Yes.** `https://slsa.dev/provenance/v1` verifies, and a deliberately wrong `--owner` is still refused. |
| A prerelease draft is marked as one | **Yes.** `prerelease=true`, where rc.1's draft was `false`. `v1.1.0` stayed Latest throughout. |
| The moving tag is still not hijacked | **Yes.** `1.1` still points at `sha256:05645a37…`; rc.2 got `sha256:0958e327…`. |

Both rc tags and their drafts are deleted. Their GHCR image versions were left at first, as above.
They were deleted on 2026-09-14 at the owner's instruction, from all three packages. Before
deleting, each rc index digest was confirmed not to be referenced by, and to share no child
manifest with, `1.0.0`, `1.1.0`, `1.2.0` or `1.2.1`. Afterwards those four tags resolved to the same
digests for all three images.

**And again before `v1.2.0`, because `release.yml` changed after rc.2 proved it.** `6fded95`
removed `osx-x64` from the RID matrix. `v1.2.0-rc.1` from `cc3aacb`, run 34767676557, 2026-09-13.
All 13 jobs passed with **no annotations** in any of them. That is one job fewer than rc.2, because
one RID fewer is published.

| Claim | Result |
| --- | --- |
| The matrix publishes exactly the seven RIDs in the first table | **Yes.** The draft carries seven archives, `SHA256SUMS` and the three SBOMs: eleven assets, and no `osx-x64`. |
| Provenance verifies from outside the workflow | **Yes**, for all three images and the `linux-x64` archive. It names this repository, `.github/workflows/release.yml`, `refs/tags/v1.2.0-rc.1` and `cc3aacb`. A deliberately wrong `--owner` is refused for both an image and the archive. |
| The SBOMs are still attested per image | **Yes.** Predicate `https://cyclonedx.org/bom`, with 36, 38 and 56 components for the API, the MCP server and the console. Those are the same figures as rc.2, which Phase 12C did not change. |
| `SHA256SUMS` matches the archive | **Yes.** `devbuddy-linux-x64.tar.gz` downloaded from the draft hashes to `e341e068…`, matching `SHA256SUMS`. |
| Both architectures are in the manifest | **Yes.** `linux/amd64` and `linux/arm64` for `devbuddy-api:1.2.0-rc.1`. This was checked for that one image only. |
| A prerelease draft is marked as one | **Yes.** `prerelease=true`, and `v1.1.0` stayed Latest. |
| The moving tag is not hijacked | **Yes.** No `1.2` tag exists for any of the three images. `1.1` still resolves to the same digest as `1.1.0` for all three, for the API `sha256:05645a37…`. |

The rc tag and its draft are deleted. **Its GHCR image versions were deleted too**, on 2026-09-14,
at the owner's instruction, unlike rc.1 and rc.2 above whose image tags are still there. The version
tagged `1.2.0-rc.1` was removed from each of the three packages. Two checks came first: the rc
index digest is not referenced by `1.2.0`'s manifest, and the two share no child manifest. Two
checks came after: `1.2.0` resolves to the same digest for all three, and its provenance and SBOM
attestations still verify. The rc's untagged child manifests and its attestation versions are left.
None of this is the `v1.2.0` checklist: it proves the workflow, not the release, and no smoke test,
Compose run or drill was performed against it.

## What was verified for v1.2.1

**Tagged from `56a4c2a`, published 2026-09-14.** A security fix: the evidence store no longer runs
as root. Since `v1.2.0`,
nothing under `src` or `web` has changed. The changes are:
- the evidence service in `docker/compose.yaml`;
- `docker/evidence/Dockerfile`;
- `DeploymentTests`;
- the supply-chain workflow's image check.

`release.yml` is unchanged since `v1.2.0-rc.1` proved it, so no throwaway prerelease tag was cut.
Nothing is carried over: the checks below ran against `fb9977b` on 2026-09-14, before the tag. The
tag may land on a later commit only if that commit changes documentation or `plugin.json` alone.

Everything below ran on jmhp. Each run used a clean clone and a Compose project of its own, and the
owner's `devbuddy` stack was not touched. `devbuddy-v121` published on 18080 and 18081, and
`devbuddy-up121` on 28080 and 28081. Only the port lines were overridden.

| Check | When | Result |
| --- | --- | --- |
| `dotnet test DevBuddy.slnx -c Release` on Linux with Docker | **Against `fb9977b`, before the tag** | **Yes.** In `mcr.microsoft.com/dotnet/sdk:10.0` against the host's Docker daemon: all six test projects ran, with 658 tests passed and none failed. That includes `DevBuddy.Infrastructure.Tests` at 197 with the two new SB-31 checks. CI had run the same suite on `0bb0daa`. |
| Compose from clean to healthy | **Against `fb9977b`, before the tag** | **Yes**, built from source, the evidence image included. All six services came up. `api` was healthy 82 seconds after the build started, `migrate` exited 0 having applied seven migrations, and `retention` logged its first pass. The API answered `/health` 200, `/operations` 401 and the UI 200. The MCP server refused `POST /` with 401, against a 404 control. Nothing listened on 5432 or 9000. |
| No service runs as root | **Against `fb9977b`, before the tag** | **Yes**, read from the host rather than the configuration. The API, MCP server and retention ran as uid 1654, PostgreSQL as uid 70, and **MinIO as uid 1000**. The evidence container had a read-only root filesystem, every capability dropped, and its volume at `/srv/evidence`, which a fresh install gave to `1000:1000`. The log volume was owned by uid 1654. |
| Tokens stay out of the log | **Against `fb9977b`, before the tag** | **Yes**, in both directions. With the default setting, a recovery request answered 202 and the API logged only that the message could not be delivered and its contents were not written. With the opt-in, the same request wrote the token. Across the whole log file on the volume there is one "not written" warning and exactly one token line, the opt-in's. **The first attempt was inconclusive and is not counted.** It ran before the drill had created the account, so neither request produced any line; an unknown address gets the same 202 and nothing else, by design. It was re-run once the account existed. |
| Destroy-and-restore drill | **Run on 2026-09-14, against `fb9977b`, with the new evidence image** | **Yes**, with both the database and the evidence volumes destroyed. See below. |
| Upgrade from `v1.2.0` | **Run on 2026-09-14, on amd64 here and on arm64 earlier** | **Yes, on both.** Starting from `v1.2.0` as shipped, with MinIO at uid 0: an artefact was captured and downloaded. The new evidence image then refused to start without the one-time `chown`, with `drive may be faulty`, as `deployment.md` says. After the `chown` it ran as uid 1000. The earlier artefact downloaded byte for byte, a new encrypted capture through the application worked, and both survived a restart with no error lines since. The same run then went from clean: the volume was owned by `1000:1000` with no manual step, capture and download worked, and every service was non-root. The arm64 run is recorded under *The non-root evidence store on `linux/arm64`* below. |

### The destroy-and-restore drill for v1.2.1

The same scripted drill as `v1.2.0`, against the running stack, with MinIO now built from
`docker/evidence/Dockerfile`.

**Before the disaster**, the installation held:
- a work item;
- a record taken through draft, submit, approve and publish, with the approval bound to content
  hash `6443D802…`;
- two evidence artefacts, of 232 and 64 bytes;
- an administrator account;
- a machine token;
- fourteen audit entries.

A file carrying a connection string and an AWS key was refused with 422 Blocked, and the store kept
two artefacts. The backup came to 12,573 bytes and was copied off its volume before the disaster.

| Row | Result |
| --- | --- |
| Records | **Back.** `get_record` returned exactly what it did before the disaster. |
| Approvals | **Back, and still bound.** The history was identical, with content hash `6443D802…` both before and after. |
| Evidence | **Back, byte for byte**, into a store running as uid 1000. The evidence volume was destroyed and recreated owned by `1000:1000`. Both artefacts downloaded at 232 and 64 bytes with matching SHA-256 hashes, so the bytes came out of the backup. |
| Audit history | **Back.** All fourteen entries were present afterwards, including `RecordApproved/Failed` and `ContentScanned/Denied`. |
| Accounts | **Back.** Sign-in with the same password succeeded. |
| Plugins | **Back.** The machine token minted before the disaster answered `list_projects` identically over MCP stdio, and a bogus token was refused with "No identity was resolved for this request". |

A second restore was refused with "This installation already has data. Restore into an empty
database." An access token issued before the disaster still answered 200 on `/me`, as documented.

### Against the release's own artefacts

Tag `v1.2.1` pushed from `56a4c2a`, run 34819658557, 2026-09-14. The draft was **published on
2026-09-14** at 08:28 UTC, at the owner's instruction, and is the repository's Latest release.

**The run failed once, and the failure was transient.** On the first attempt the `Push and attest
mcp` job failed in its `Set up Buildx` step, before building anything. The runner could not pull
`moby/buildkit` from Docker Hub: `connection reset by peer`. The api and cli jobs had already
pushed `1.2.1`, so the moving `1.2` tag briefly pointed at 1.2.1 for those two and 1.2.0 for mcp,
and no draft existed. Only the failed job and its dependent were re-run, as attempt 2, and both
passed. The latest attempt carries no annotations.

| Check | When | Result |
| --- | --- | --- |
| The draft carries exactly the published RIDs | Re-run | **Yes.** Seven archives, `SHA256SUMS` and the three SBOMs; `prerelease=false`, and `v1.2.0` stayed Latest while it was a draft. |
| Attestations verify from outside the workflow | Re-run, after the build | **Yes**, for all three images and all seven archives: provenance names `refs/tags/v1.2.1` and `56a4c2a`. A deliberately wrong `--owner` is refused for an image and for an archive. |
| `SHA256SUMS` matches the published archives | Re-run | **Yes**, all seven downloaded from the draft, and again on every machine an archive was copied to. |
| SBOM attached per image | Re-run | **Yes.** CycloneDX: 36, 38 and 56 components for the API, the MCP server and the console. |
| Both architectures in every manifest | Re-run | **Yes.** `linux/amd64` and `linux/arm64` for all three images. `1.2.1`, `1.2` and `v1.2.1` resolve to one digest per image, mcp included once attempt 2 had pushed it. `1.2.0` still resolves to its own. |
| Images **started** on `linux/amd64` | Re-run, against the published images | **Yes**, on jmhp: pulled as `1.2.1` and confirmed against the published digest; `amd64`; user `1654`. The console applied seven migrations and listed the twenty catalogue names. The API answered 200, 401 and 200. The MCP server answered 401, against a 404 control. |
| Images **started** on `linux/arm64` | Re-run, against the published images | **Yes, on hardware:** the Mac mini's Docker Desktop Linux VM on the Apple M4. The same checks and results, with `arm64` and the same digests. |
| `win-x64` smoke test | Re-run | **Not run natively; run under emulation.** The development machine is Windows 11 Pro 10.0.26200 on AMD64, and Smart App Control **blocked this archive**. `DevBuddy.Cli.exe` could not load `DevBuddy.Cli.dll` (`0x800711C7`, "An Application Control policy has blocked this file"). Code Integrity logged events 3033 and 3077 under policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`, for not meeting the signing level. The `v1.2.0` archive had run on the same machine. The assemblies are unsigned in both, and Smart App Control decides on reputation, which a new build does not have. The setting was left on. The same archive then ran **under x64 emulation** on Windows on ARM, in a VMware guest on Apple silicon. Twenty operations, identical to the catalogue, exit 0; `--every 24` refused. So for this release the row's "Run" is **not native**. |
| `linux-x64` smoke test | Re-run | **Yes.** `ubuntu:24.04` with `libicu74`, `x86_64`, on jmhp. Twenty operations, identical; `--every 24` refused. |
| `linux-musl-x64` smoke test | Re-run | **Yes.** `alpine:3` (3.24) with `libstdc++`, `libgcc` and `icu-libs`, `x86_64`, on jmhp. Twenty operations, identical; `--every 24` refused. |
| `osx-arm64` smoke test | Re-run | **Yes, on hardware.** Natively on the Apple M4 Mac mini, macOS 26.6.2. Twenty operations, identical; `--every 24` refused; the apphost is ad-hoc signed. |
| `linux-arm64` smoke test | Re-run | **Yes, twice.** First in `ubuntu:24.04` (24.04.5) in Docker Desktop's Linux VM on the Apple M4. Then natively on Ubuntu 26.04.1 in a VMware guest on arm64. Twenty operations, identical, both times. |
| `win-arm64` smoke test | Run for this release, not required | **Yes, in a VM.** Natively on Windows on ARM in a VMware guest on Apple silicon. Twenty operations, identical, exit 0; `--every 24` refused; `migrate` with no connection string reached the refusal inside Infrastructure. The RID stays built but unverified. |
| `linux-musl-arm64` smoke test | Run for this release, not required | **Yes, in a container on a VM.** `alpine:3` (3.24) in Docker on the Ubuntu 26.04 VMware guest, `aarch64`. Twenty operations, identical; `--every 24` refused. The RID stays built but unverified. |

The published images are the three application images. The evidence image is built by Compose
where it runs, and was verified above: before the tag, on amd64 and arm64.

**What this release teaches the checklist.** A native `win-x64` smoke test on the development
machine can no longer be assumed. Smart App Control may refuse any new unsigned build there, and
switching it off is not this project's call. Until the assemblies are signed, or another Windows x64
machine without it is available, the row can be run only under emulation. Every release has to say
so.

## What was verified for v1.2.0

**Tagged from `7512240`, published 2026-09-14.** The first table records the checks that do not
need the release's artefacts. They were run against `3160f61` on 2026-09-13, before the tag.
Outside `docs/`, `7512240` is identical to `3160f61`. The checks against the published archives
and images follow under *Against the release's own artefacts*.

Everything below ran on jmhp. It used a clean clone and a Compose project of its own,
`devbuddy-v120`. The owner's own `devbuddy` stack on that machine was not touched, and neither were
its volumes. That stack holds 8080 and 8081, so this one published the API and the MCP server on
18080 and 18081 through an override of those two port lines. Nothing else in `docker/compose.yaml`
was changed.

| Check | When | Result |
| --- | --- | --- |
| `dotnet test DevBuddy.slnx -c Release` on Linux with Docker | **Against `3160f61`, before the tag** | **Yes.** It ran in `mcr.microsoft.com/dotnet/sdk:10.0` against the host's Docker daemon, so the Testcontainers and drill tests actually ran. All six test projects ran, with 656 tests passed and none failed. |
| Compose from clean to healthy | **Against `3160f61`, before the tag** | **Yes.** All six services came up. `api` was healthy, `migrate` exited 0, and `retention` logged its first pass with all six counts. `/health` returned 200, `/operations` 401 and the UI 200 at the root. The MCP server answered `POST /` with 401, against a control showing an unmapped path answers 404. Neither 5432 nor 9000 was listening on the host. The log volume came back owned by uid 1654 with a dated file in it. Compose builds from source, so this exercised the Dockerfiles rather than the published images. |
| Tokens stay out of the log | **Against `3160f61`, before the tag** | **Yes**, in both directions against the running stack. With the default, a recovery request answered 202. The API logged that the message could not be delivered and wrote no token, to stdout or to the file on the volume. With `DEVBUDDY_EMAIL_ALLOW_TOKENS_IN_LOG=true`, the same request wrote the token, under the line marking it sensitive. That token appears exactly once in the log file and nowhere in the default run's output. The API was put back on the default afterwards. |
| Destroy-and-restore drill | **Run on 2026-09-13, against `3160f61`** | **Yes**, with both the database and the evidence volumes destroyed. See below. |

### The destroy-and-restore drill for v1.2.0

This was scripted rather than clicked through the web UI. It still ran against the running stack,
through the same console commands and HTTP routes a person would use.

**Before the disaster**, the installation held:
- a work item;
- a record taken through draft, submit, approve and publish, with the approval bound to content
  hash `AFB537E1…`;
- two evidence artefacts, one of 232 bytes of text and one of 64 random bytes;
- an administrator account;
- a machine token;
- fourteen audit entries.

A file carrying a connection string and an AWS key was refused on upload with 422 Blocked. The store
still held two artefacts afterwards, which is SB-17 against the running stack. The backup was
copied off its volume before the disaster, and came to 12,552 bytes.

| Row | Result |
| --- | --- |
| Records | **Back.** `get_record` returned exactly what it did before the disaster: `Published`, revision 1, title, body and provenance unchanged. |
| Approvals | **Back, and still bound.** The history is identical to its pre-disaster copy. The revision's content hash and the approval's `approvedContentHash` are both `AFB537E1…`, the approver is named, and `approverWasDraftCreator` is true. |
| Evidence | **Back, byte for byte.** Both artefacts downloaded at 232 and 64 bytes, and their SHA-256 matched the files that went in (`ebd4fdc7…`, `acb675af…`). The evidence volume had been destroyed, so those bytes came out of the backup. |
| Audit history | **Back.** All fourteen entries are present afterwards, and none is missing. They include `RecordApproved/Failed` from an approval attempted before submission, and the `ContentScanned/Denied` entry for the blocked file. The three later entries are the restore's own reads. |
| Accounts | **Back.** Sign-in with the same password succeeded. |
| Plugins | **Back.** The machine token minted before the disaster still resolves over MCP stdio. `list_projects` returned an answer identical to the one before. The control: a bogus token was refused with "No identity was resolved for this request". |

A second restore was refused with "This installation already has data. Restore into an empty
database." It exited 1, and the record count stayed at one. The `retention` service came back with
the stack and logged a pass.

As in the `v1.1.0` drill, an access token issued before the disaster still answered 200 on `/me`
afterwards. A restore does not revoke a stateless JWT inside its lifetime, and
`backup-and-restore.md` already says so.

### Against the release's own artefacts

Tag `v1.2.0` was pushed from `7512240`, run 34768949931, 2026-09-13. The draft was **published on
2026-09-14** at 03:32 UTC, and was the repository's Latest release until `v1.2.1` the same day. Outside `docs/`, `7512240` is identical to `3160f61`, so the checks above stand for
it. All 13 jobs passed, with no annotations.

| Check | When | Result |
| --- | --- | --- |
| The draft carries exactly the published RIDs | Re-run | **Yes.** Seven archives, `SHA256SUMS` and the three SBOMs, and no `osx-x64`. It is marked `prerelease=false`, and `v1.1.0` stayed Latest while it was a draft. |
| Attestations verify from outside the workflow | Re-run, after the build | **Yes.** All three images, and every archive smoke-tested below. Provenance names `refs/tags/v1.2.0` and `7512240`. A deliberately wrong `--owner` is refused for an image and for an archive. |
| `SHA256SUMS` matches the published archives | Re-run | **Yes**, for all five archives downloaded from the draft, and again on each machine they were copied to. |
| SBOM attached per image | Re-run | **Yes.** CycloneDX: 36, 38 and 56 components for the API, the MCP server and the console. |
| Both architectures in every manifest | Re-run | **Yes.** `linux/amd64` and `linux/arm64` for all three images. `1.2.0`, `1.2` and `v1.2.0` resolve to one digest per image. `1.1` still resolves to `v1.1.0`'s. |
| `win-x64` smoke test | Re-run | **Yes.** Natively on Windows 11 Pro 10.0.26200, AMD64, from the published archive. `operations --ai` exited 0 with nothing on stderr and listed exactly the catalogue's twenty names. `retention --every 24` was refused with its reason. `migrate` with no connection string got as far as refusing for the missing connection string, which happens inside Infrastructure. |
| `linux-x64` smoke test | Re-run | **Yes.** `ubuntu:24.04` with `libicu74`, `x86_64`, on jmhp. Twenty operations, identical to the catalogue, exit 0; `--every 24` refused. |
| `linux-musl-x64` smoke test | Re-run | **Yes.** `alpine:3` (3.24) with `libstdc++`, `libgcc` and `icu-libs`, `x86_64`, on jmhp. Twenty operations, identical to the catalogue, exit 0; `--every 24` refused. |
| `osx-arm64` smoke test | Re-run | **Yes, on hardware.** Natively, on the Apple M4 Mac mini running macOS 26.6.2. Twenty operations, identical to the catalogue, exit 0; `--every 24` refused. `codesign -dv` reports the apphost as ad-hoc signed. |
| `linux-arm64` smoke test | Re-run | **Yes, on hardware, for the first time.** `ubuntu:24.04` (24.04.5) with `libicu74`, `aarch64`, inside Docker Desktop's Linux VM on the same Apple M4. This is a virtual machine on an arm64 CPU, not QEMU. Twenty operations, identical to the catalogue, exit 0; `--every 24` refused. |
| Images **started** on `linux/amd64` | Re-run, against the published images | **Yes**, on jmhp. Each image was pulled as `1.2.0` and confirmed against the published digest, and each reports `amd64` and user `1654`. Against a throwaway `postgres:17-alpine`: the console applied all seven migrations and listed the twenty catalogue names. The API answered `/health` 200, `/operations` 401 and the UI 200 at the root. The MCP server refused `POST /` with 401, against a control showing an unmapped path answers 404. |
| Images **started** on `linux/arm64` | Re-run, against the published images | **Yes, on hardware, for the first time.** The same checks on the Mac mini's Docker Desktop, whose Linux VM runs on the Apple M4. All three report `arm64` and user `1654` at the same digests. The console applied all seven migrations against `postgres:17-alpine` and listed the twenty names. The API answered 200, 401 and 200, and the MCP server answered 401 against a 404 control. |
| `win-arm64` smoke test | **First run ever, 2026-09-14**, after the tag | **Yes, in a VM.** Windows 10.0.26200 on ARM64 in a VMware guest (`VMware20,1`) whose processor reports "Apple silicon", from the published archive, its SHA-256 matching `SHA256SUMS`. Twenty operations, identical to the catalogue, exit 0 with nothing on stderr; `--every 24` refused; `migrate` with no connection string reached the missing-connection-string refusal inside Infrastructure. The RID **stays in the built-but-unverified tier**. Moving it is the owner's decision, and this was one run in a VM rather than a native Windows on ARM device. |
| `linux-musl-arm64` smoke test | **First run ever, 2026-09-14**, after the tag | **Yes, on hardware.** `alpine:3` (3.24) with `libstdc++`, `libgcc` and `icu-libs`, `aarch64`, in Docker Desktop's Linux VM on the Apple M4, from the published archive. Twenty operations, identical to the catalogue, exit 0; `--every 24` refused. This confirms the dependency list the x64 musl run implied. The RID **stays in the built-but-unverified tier** until the owner decides otherwise. |
| `linux-arm64`, second run | 2026-09-14 | **Yes.** Natively on Ubuntu 26.04.1 (kernel 7.0, ICU 78) in a VMware guest on arm64, from the same archive. Twenty operations, identical to the catalogue, exit 0; `--every 24` refused. The first ICU 78 system any archive has run on. |

Two things these runs changed about what the matrix can say.
- **`linux/arm64` has now started on arm64 hardware**, for both the native `linux-arm64` archive and
  the three images. Every earlier arm64 row was emulated. This is still a Linux VM under Docker
  Desktop on macOS, not a Graviton or a Raspberry Pi. What it does show is that nothing was being
  hidden by QEMU.
- **jmhp has no arm64 emulation installed.** Installing it means registering a binfmt handler in the
  host kernel, which is a change to that machine rather than a test run on it. It was not done. The
  Mac mini made it unnecessary.

**The draft was publishable, and was published on 2026-09-14.** Its release notes state that
`win-arm64` and `linux-musl-arm64` stay unsupported and in the built-but-unverified tier. They also
say that each was started once for this release: one in a VM, the other in a container.

### The Compose stack from clean on `linux/arm64`, after publication

Run on 2026-09-14 from the `v1.2.0` tag at `7512240`, in an Ubuntu 26.04.1 VMware guest on arm64
(2 CPUs, 5.3 GB of memory). The machine ran Docker 29.1.3 and Compose 2.40.3. It used the
**published images**, not a build. An override replaced every `build:` with the
`ghcr.io/…:1.2.0` image, and nothing else in `docker/compose.yaml` changed. A build from source
was not attempted on a machine that size. This is the first run of the stack on arm64.

| Check | Result |
| --- | --- |
| From clean to healthy | **Yes.** The project started with no volumes, and `up` ran with no build and no pull. All six services came up. `api` was healthy after 17 seconds, and `migrate` exited 0 having applied seven migrations. `retention` logged its first pass with all six counts. |
| Every container on arm64 | **Yes.** All five images report `arm64`: the three application images, `postgres:17-alpine`, and the pinned MinIO digest. |
| HTTP | **Yes.** The API answered `/health` 200, `/operations` 401 and the UI 200 at the root. The MCP server refused `POST /` with 401, against a control showing an unmapped path answers 404. |
| Nothing else reachable from the host | **Yes.** Only `127.0.0.1:8080` and `127.0.0.1:8081` are published, and nothing listens on 5432 or 9000. |
| Log volume | **Yes.** It came back owned by uid 1654, with a dated file in it. |
| `down` then `up` keeps the data | **Yes.** All five volumes survived `down` without `-v`. The second `up` was healthy, and `migrate` reported "The database is already up to date." |
| No service runs as root | **No, for the object store.** On the host, the application processes run as uid 1654 and PostgreSQL as uid 70. **MinIO runs as uid 0.** Its image sets no user, and neither does `docker/compose.yaml`. Your amd64 stack on jmhp runs the same digest and shows the same thing: MinIO as uid 0, with no user-namespace remapping. So "No service runs as root" in `docker/compose.yaml` and `deployment.md` is not true of the evidence store, and it was not true in `v1.0.0` or `v1.1.0` either. SB-31's two checks cover the three application images only: a `USER` line in each Dockerfile, and a non-root `Config.User` in each built image. Neither looks at a third-party image. MinIO publishes no port and sits on the internal network only, which limits who can reach it; it does not make it non-root. |

Not claimed: a build from source on arm64, the destroy-and-restore drill on arm64, and any
server-class arm64 host.

### The non-root evidence store on `linux/arm64`, 2026-09-14 (released in `v1.2.1`)

The fix for the finding above, run on the same VM before any release carries it. The evidence
service was built from the new `docker/evidence/Dockerfile`, natively on arm64. The other services
used the published `1.2.0` images, as above. The run started from the `v1.2.0` stack itself, with
MinIO as uid 0 and an artefact already stored.

| Check | Result |
| --- | --- |
| Before: an artefact stored by the root-run MinIO | **Yes.** 4,096 random bytes captured through the API with 200, and downloaded byte for byte. |
| The new image without the one-time ownership change | **Refuses, as documented.** The service restart-loops unhealthy with `file access denied, drive may be faulty`. |
| After `chown -R 1000:1000` of the existing volume | **Yes.** Healthy; MinIO runs as uid 1000 on the host, on a read-only root filesystem, with every capability dropped. |
| Old artefact after the upgrade | **Yes**, byte for byte. |
| A new artefact through the application | **Yes.** Captured with 200 and downloaded byte for byte. The application asks for AES256 on every object, and a store without its key refuses the write with a 500, so the store accepted the encrypted write. |
| Both artefacts after restarting the evidence store | **Yes**, byte for byte. There were no error lines in MinIO's log since the ownership change. |
| From clean | **Yes.** After `down -v` and `up`: the new evidence volume is owned by `1000:1000` with no manual step. Capture and download work. Every service is non-root on the host: API, MCP server and retention at 1654, PostgreSQL at 70, MinIO at 1000. |

Not claimed: this on amd64, where the change is identical but was not run, the destroy-and-restore
drill with the new image, and a release carrying it.

## What was verified for v1.1.0

Built from `4253a5b`. Tag `v1.1.0`, run 34468792224, **published 2026-09-10**.

Nothing is carried over. The 2026-09-10 decision closed that licence, and this release changed the
console, the fallback email sender, all three Dockerfiles and the Compose stack, so there was
nothing it could honestly have carried anyway.

| Check | When | Result |
| --- | --- | --- |
| All 14 workflow jobs | Re-run | **Yes.** The full suite, the format check and the web build gated it; eight RIDs published; three images pushed and attested. |
| Attestations verify from outside the workflow | Re-run, after the build | **Yes.** All three images and the `linux-x64` archive. Provenance names this repository, `.github/workflows/release.yml`, `refs/tags/v1.1.0` and source commit `4253a5b`. Checked against a negative control — a deliberately wrong `--owner` is refused for both an image and an archive — so a pass means something. |
| `SHA256SUMS` matches the published archive | Re-run | **Yes.** `devbuddy-linux-x64.tar.gz` downloaded from the draft release hashes to `a183cd57…`, matching both `SHA256SUMS` and the attested digest. |
| SBOM attached per image | Re-run | **Yes.** CycloneDX, still distinct per host: 36, 38 and 56 components for the API, the MCP server and the console. |
| Both architectures in every manifest | Re-run | **Yes.** `docker buildx imagetools inspect` reports `linux/amd64` and `linux/arm64` for all three images, plus the two buildx attestation manifests. This is the first release to carry arm64 at all. |
| `linux/arm64` images **started** | Re-run, against the published images | **Yes.** Pulled by digest: the console reports `arm64`, user `1654`, and applied all six migrations against `postgres:17-alpine`; the API answered `/health` 200, `/operations` 401 and the UI 200 at the root; the MCP server refused `POST /` with 401 against a control showing an unmapped path answers 404. Under emulation, not hardware. |
| `win-x64` smoke test | Re-run | **Yes.** Natively on the development machine, from the published archive. Eighteen AI-exposed operations, exit 0. The new `retention --every` flag is present in the shipped binary and `--every 24` is refused with the reason rather than read as twenty-four days. |
| `linux-x64` smoke test | Re-run | **Yes.** `ubuntu:24.04` with `libicu74`, `uname -m` reporting `x86_64`, from the published archive. Eighteen operations. |
| The new capability stays off the AI surface | Re-run | **Yes**, in the shipped binaries on both platforms: neither `capture_evidence` nor `publish_record` appears in `operations --ai`, and nothing Phase 12 added is an operation at all. |
| Compose from clean to healthy | **Against `4253a5b`, before the tag** | **Yes**, and it is the same commit rather than an earlier one. All five services up, `api` healthy, `migrate` exited 0, and the new `retention` service up with its first pass logged. The log volume came back owned by the application user with a rolled file in it. Compose builds from source, so this exercised the Dockerfiles rather than the published images. |
| Tokens stay out of the log | **Against `4253a5b`, before the tag** | **Yes**, both directions against the running stack. A real recovery request answered 202 and the API logged only that the message could not be delivered, with no token in stdout or in the file on the volume; with `DEVBUDDY_EMAIL_ALLOW_TOKENS_IN_LOG=true` the same request wrote the token to both. |
| `linux-arm64` smoke test | Re-run | **Yes.** `ubuntu:24.04` under `linux/arm64` with `libicu74`, `uname -m` reporting `aarch64`, from the published archive. Eighteen operations, exit 0, and `--every 24` refused with its reason. Emulated, not hardware. |
| `linux-musl-x64` smoke test | Re-run | **Yes.** `alpine:3` (3.24.1) with `libstdc++`, `libgcc` and `icu-libs`, `x86_64`, from the published archive. Eighteen operations, exit 0, and nothing Phase 12 added on the AI surface. |
| Destroy-and-restore drill | **Re-run by hand, 2026-09-10** | **Yes, and against a harder disaster than the documented one.** See below. |
| `osx-arm64`, `osx-x64`, `win-arm64`, `linux-musl-arm64` | — | **Not run, again.** Built and published as-is, per the 2026-09-10 decision to keep shipping them. No macOS and no Windows on ARM available. The release notes must say this verbatim. |

### The destroy-and-restore drill for v1.1.0

Performed by hand on 2026-09-10 against the stack built from `4253a5b`. **Both** the database and
the evidence volumes were destroyed, not the database alone as `backup-and-restore.md` describes.
That matters: with the evidence volume surviving, the artefact bytes were never actually lost, so
the row that catches "rows came back and bytes did not" was the one row the documented drill could
not really exercise.

What was there before the disaster: a work item, a record taken through draft, submit, approve and
publish with the approval bound to its content hash, two evidence artefacts (310 bytes of text and
64 random bytes), an account, a machine token, and thirteen audit entries. A file carrying a
connection string and an AWS key was refused on the way in, with nothing written and the store
still holding two artefacts — SB-17 against the running stack rather than in a test.

| Row | Result |
| --- | --- |
| Records | **Back.** `Published`, revision 1, title and 295-byte body intact, provenance author preserved. |
| Approvals | **Back, and still bound.** The revision's content hash and the approval's `approvedContentHash` are both `52B97230…`, the same value recorded before the disaster, with the approver named and `approverWasDraftCreator` true. |
| Evidence | **Back, byte for byte.** Both artefacts downloaded at 310 and 64 bytes and hashed to `ff51e494…` and `8f54a27b…`, identical to what went in. The evidence volume had been destroyed, so those bytes came out of the backup. |
| Audit history | **Back.** All thirteen entries, including both refusals — `RecordApproved` and `RecordPublished` each appear once Failed and once Succeeded, from a premature approve attempt — and the `ContentScanned`/`Denied` entry for the blocked file. Later entries are the restore's own reads. |
| Accounts | **Back.** Sign-in with the same password succeeded. See the qualification below. |
| Plugins | **Back.** The machine token minted before the disaster still resolves over MCP stdio and returns the same answer as before, against a control showing a bogus token is refused with "No identity was resolved for this request". |

Restoring a second time was refused with "This installation already has data. Restore into an empty
database." The `retention` service came back up with the stack and logged a pass.

**One thing the drill qualified rather than confirmed.** `backup-and-restore.md` says "Sign in.
Everybody will have to; sessions are not restored." The refresh-session rows are indeed not in a
backup, and signing in again works — but an **access token issued before the disaster still
validated afterwards**, because it is a stateless JWT signed with the same key and was inside its
lifetime. A restore does not and cannot revoke one. Nothing here is broken, and the sentence is
narrower than it reads: what a restore drops is the ability to *refresh*, not tokens already
issued.

**The draft was publishable, and was published the same day.** Every row the checklist asks for
has been run for this tag, except the four RIDs the 2026-09-10 decision keeps shipping without ever
starting, which the release notes must state verbatim.

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

## What was verified on 2026-09-13 (not a release)

Recorded here for the reason the Phase 12 entry above gives. These are self-contained executables
built from `main` at `6fded95` the way `.github/workflows/release.yml` builds them —
`dotnet publish -c Release -r <rid> --self-contained true` for all three hosts, in
`mcr.microsoft.com/dotnet/sdk:10.0` on a Linux x64 host — and archived the same way. None of them is
a published artefact, except the one row that says so.

The check is the one this file defines: `DevBuddy.Cli operations --ai` starts and answers, and the
names it lists are the catalogue's. At `6fded95` the catalogue marks twenty operations as available
to AI, and every run of that build listed exactly those twenty names.

| RID | Where it ran | Result |
| --- | --- | --- |
| `linux-x64` | A fresh `ubuntu:24.04` container on an x64 host, with `libicu74` installed as the native dependencies above require. | **Yes.** Exit 0, twenty operations, the catalogue's names, nothing on stderr. |
| `win-x64` | Natively, on the machine this repository is developed on. | **Yes.** Exit 0, twenty operations, the catalogue's names, nothing on stderr. |
| `osx-arm64` | Natively, on an Apple M4 Mac mini running macOS 26.6.2. Hardware, not emulation. | **Yes.** Exit 0, twenty operations, the catalogue's names, nothing on stderr. |
| `osx-arm64`, the published `v1.1.0` archive | The same Mac mini, from the release download, its SHA-256 matching `SHA256SUMS`. | **Yes.** Exit 0 and eighteen operations, which is that release's catalogue: `search_similar_records` and `list_records` joined the AI surface on 2026-09-11, after `v1.1.0`. |

Two things these runs settled rather than assumed.

- **A Linux-built `osx-arm64` executable starts on Apple silicon.** macOS on arm64 refuses to run
  an unsigned executable, and the release builds this RID on `ubuntu-latest`, so it was a fair worry
  that the archive had never been startable at all. It is not the case: `codesign -dv` reports the
  apphost as ad-hoc signed, for the `v1.1.0` archive and for the `6fded95` build alike, so the .NET
  SDK signs it even when publishing from Linux.
- **Smart App Control on the development machine did not stop `win-x64`, and it does stop local
  test runs there.** Smart App Control is on, and every DevBuddy assembly in the self-contained build
  is unsigned. The executable ran all the same, and not only `operations --ai`, which may never load
  the infrastructure assembly: `migrate` with no connection string got as far as refusing for the
  missing connection string, which happens inside `DevBuddy.Infrastructure.dll`. What the policy does
  block is `dotnet test` on that machine. The code integrity log records `DevBuddy.Infrastructure.dll`
  from the build output refused under policy `{0283AC0F-FFF1-49AE-ADA1-8A933130CAD6}` for not meeting
  the required signing level, so the Windows test projects are verified by CI on `windows-latest`,
  not locally.

**What this did not change by itself.** One run outside a release does not put a platform in the
verified tier, because that tier means "smoke-tested by hand every release". The owner moved
`osx-arm64` there the same day, under ADR-0008 and `info.md`, and the table at the top reflects it. Not claimed: the API or MCP executables on any of these three platforms; a macOS host
with nothing installed but the operating system, since the Mac mini also carries a system-wide .NET
installation that a self-contained executable does not use; and any run of `win-arm64` or
`linux-musl-arm64`.

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
