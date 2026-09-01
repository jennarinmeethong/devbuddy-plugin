# ADR-0008: Self-contained publish tiers and Docker packaging

- Status: Accepted
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
| Verified | `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64` | Built and smoke-tested in CI every release. |
| Built, unverified | `win-arm64`, `osx-x64`, `linux-musl-x64`, `linux-musl-arm64` | Published as-is, labelled unverified in the release notes. |
| Not published | Everything else | Out of scope for v1. Not claimed as supported. |

Container images are a **separate** matrix: `linux/amd64` and `linux/arm64` only, on chiseled
ASP.NET base images, running as a non-root user.

Native OS dependencies are documented explicitly, including ICU and OpenSSL on Linux unless
invariant globalization is enabled. No single-file and no Native AOT claim is made. The matrix is
rechecked against the .NET 10 supported-OS list at each release.

## Consequences

- The middle tier is the honest part of this decision: those artifacts are useful and untested, and
  saying so is better than either hiding them or implying they are verified.
- CI cost is bounded, because only four RIDs are smoke-tested.
- Users on an unverified RID carry the risk knowingly.

## Alternatives considered

- **Verify everything.** Long CI, hardware we do not have for every target. Rejected as unrealistic.
- **Publish one RID.** Contradicts the requirement to target the supported combinations.
- **Publish everything and stay silent about verification.** Rejected: that is the
  universal-compatibility claim `info.md` forbids.
