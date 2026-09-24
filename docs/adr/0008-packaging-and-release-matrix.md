# ADR-0008: Self-contained publish tiers and Docker packaging

- Status: Accepted; amended 2026-09-07, 2026-09-10, 2026-09-13, 2026-09-14, 2026-09-21 and 2026-09-22
- Date: 2026-09-01
- Phase: 10

## Context

`info.md` requires self-contained .NET artifacts targeting the OS and architecture combinations
supported by .NET 10 for the console and server workloads in scope, an explicit runtime identifier
and verification matrix, documented unsupported combinations rather than universal-compatibility
claims, Docker packaging alongside the native artifacts with a separately defined container matrix,
and self-hosted deployment on user-controlled infrastructure. It also notes that self-contained
deployment includes the runtime but can still require native OS dependencies, and does not imply a
single file or Native AOT. The project owner confirmed the tier approach on 2026-09-01.

## Decision

Publish in three declared tiers, recorded in `docs/operations/release-matrix.md`:

| Tier | RIDs | Meaning |
|---|---|---|
| Verified | `linux-x64`, `linux-arm64`, `win-x64`, `linux-musl-x64`, `osx-arm64`, `linux-musl-arm64` | Built and smoke-tested by hand every release, recorded in that file. |
| Built, unverified | `win-arm64` | Published as-is, not smoke-tested per release, labelled unverified in the release notes. |
| Not published | Everything else | Out of scope for v1. Not claimed as supported. |

Container images are a **separate** matrix: `linux/amd64` and `linux/arm64` (see the second
amendment below), on chiseled base images, running as a non-root user.

Native OS dependencies are documented explicitly, including ICU and OpenSSL on Linux unless
invariant globalization is enabled. No single-file and no Native AOT claim is made. The matrix is
rechecked against the .NET 10 supported-OS list at each release.

## Amendment — 2026-09-07

The tiers above are as amended. As decided on 2026-09-01 they read: verified (`linux-x64`,
`linux-arm64`, `win-x64`, `osx-arm64`), built-but-unverified (`win-arm64`, `osx-x64`, musl
variants), and container images covering `linux/amd64` **and `linux/arm64`**.

`v1.0.0` met neither of the last two. No macOS is available to this project, so `osx-arm64` was
published without ever being run and moves down a tier; no `linux/arm64` image is built, so the
container matrix loses that row rather than claiming it. `linux-musl-x64` moves the other way — it
has been run on `alpine:3` each release — and the phrase "smoke-tested in CI" is corrected, because
those smoke tests are performed by hand: CI runs the full test suite on `ubuntu-latest` and
`windows-latest` and nothing else. The project owner chose to amend rather than hold the release
until the original tiers were met; `info.md` carries that decision.

## Amendment — 2026-09-10

**`linux/arm64` container images are built again**, which un-does half of the 2026-09-07 amendment
rather than adding something new: the 2026-09-01 decision named both architectures, the build was
never made, and the honest response at the time was to narrow the claim. Phase 12B made the build,
so the container matrix is `linux/amd64` and `linux/arm64` — as originally decided.

The three Dockerfiles cross-compile rather than emulate: the SDK stage is pinned to the builder's
architecture with `--platform=$BUILDPLATFORM` and `-a $TARGETARCH` decides the output. Nothing RUNs
in a runtime stage, so the compiler never goes through QEMU and an arm64 image costs a release
minutes instead of hours. Emulating the whole build works and is how a platform ends up quietly
deleted a year later.

All three images were **started** on arm64, not merely built — under emulation, because no arm64
hardware is available here, which is the same standard the native `linux-arm64` RID row already
held and is recorded that way in `docs/operations/release-matrix.md`. The release workflow checks
the non-root user per architecture, because a manifest list can hold one image that drops root and
one that does not.

The four unverified RIDs are **not** affected. The project owner confirmed on 2026-09-10 that
`osx-arm64`, `osx-x64`, `win-arm64` and `linux-musl-arm64` keep shipping in the middle tier, with
every release's notes saying they were never started, rather than acquiring the hardware or
dropping them. The middle tier is doing exactly the job this ADR says it is for.

## Amendment — 2026-09-13

**`osx-x64` leaves the matrix.** The project owner removed it from the platforms this project
supports, so it moves from built-but-unverified to not published. The release workflow no longer
builds it, and no later release carries an archive for it. Releases tagged before this date did
publish one, and those are left as they were released.

This narrows the 2026-09-10 amendment above from four unverified RIDs to three: `osx-arm64`,
`win-arm64` and `linux-musl-arm64` keep shipping in the middle tier on the same terms. The table in
the decision reflects it.

## Amendment — 2026-09-13, later the same day

**`osx-arm64` returns to the verified tier**, where the 2026-09-01 decision put it and from which the
2026-09-07 amendment moved it because no macOS was available. A Mac mini now is. The build was run on
it natively, both the Linux-built executable for the current source and the published `v1.1.0`
archive, and both started and answered; `docs/operations/release-matrix.md` records the runs. The
project owner moved it after those runs.

It commits to what every verified row commits to: a smoke test by hand before each release,
recorded per release, and a release for which it cannot be run records its "Run" column as no. The
middle tier is now `win-arm64` and `linux-musl-arm64`.

## Amendment — 2026-09-14

**The middle tier is no longer "never run".** Both of its RIDs were started for the first time
against the `v1.2.0` draft's archives. `win-arm64` ran in a VMware virtual machine on Apple silicon.
`linux-musl-arm64` ran in `alpine:3` inside Docker Desktop's Linux VM on the Apple M4 Mac mini. Both
listed the catalogue's AI operations; `docs/operations/release-matrix.md` records the runs.

The project owner kept both in the middle tier (`info.md`, 2026-09-14). One run in a VM or a
container does not commit anyone to a smoke test before every release, and that commitment is what
the verified tier means. The table's "never run" now reads as "not smoke-tested per release".
Release notes say what was run for these two for that release, rather than that they were never
started.

## Amendment — 2026-09-21: `linux-musl-arm64` moves to the verified tier

`linux-musl-arm64` was started, and passed the smoke test, for `v1.2.0`, `v1.2.1`, `v1.3.0` and
`v1.4.0`, in `alpine:3` on arm64 (the Mac mini's Docker Desktop VM, then the Ubuntu arm64 guest).
The owner moved it into the verified tier as Phase 13 item B10 (`info.md`, 2026-09-21), so every
release now owes that smoke test and records it. `win-arm64` stays in the built-but-unverified tier
by the same decision; it is the only row left there.

## Amendment — 2026-09-22: skipped `win-arm64` evidence is backfilled

At the owner's instruction, the published `win-arm64` archives skipped at release time were run
after publication. Together with the releases already tested, every published release has now
passed the native CLI smoke test on the Windows on ARM VMware guest. The backfilled archives'
checksums and Sigstore provenance were verified too.

This is an evidence amendment, not a tier change. The verified tier means committing to the smoke
test before every future release. The owner has not made that commitment for `win-arm64`, so it
remains the sole built-but-unverified RID.

## Amendment — 2026-09-24: `win-arm64` is verified as a client

The owner decided that `win-arm64` is a client platform (`info.md`, 2026-09-24):
- **The console and the MCP server over stdio** are smoke-tested from the published archive before
  every release is published, on the Windows on ARM VMware guest. That is a per-release commitment,
  which is what the verified tier means.
- **The API**, which is in the same archive, is **not supported** on `win-arm64`, and is not
  tested there.

No RID is left in the built-but-unverified tier once the first such run is recorded. The tier
itself stays defined, for the next RID that needs it.

## Consequences

- The middle tier is the honest part of this decision: those artifacts are useful and untested, and
  saying so is better than either hiding them or implying they are verified.
- Verification cost is bounded, because only five RIDs are smoke-tested — by hand, which bounds it
  by somebody's time rather than by CI minutes, and is why the record of who ran what and when
  lives in `docs/operations/release-matrix.md`.
- Users on an unverified RID carry the risk knowingly.
- An arm64 container host is now served, and the row says "emulated" so that nobody reads it as a
  claim about a Graviton or an Apple-silicon machine.

## Alternatives considered

- **Verify everything.** Long CI, hardware we do not have for every target. Rejected as unrealistic.
- **Publish one RID.** Contradicts the requirement to target the supported combinations.
- **Publish everything and stay silent about verification.** Rejected: that is the
  universal-compatibility claim `info.md` forbids.
