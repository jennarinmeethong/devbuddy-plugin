# Release Matrix

**Status: PLANNED. Nothing in this file has been built or verified yet.** Publishing is Phase 10.
This file exists now because the tier decision was confirmed on 2026-09-01 (see
[ADR-0008](../adr/0008-packaging-and-release-matrix.md)), and because the shape of the matrix
constrains how the hosts are written.

`info.md` requires an explicit runtime identifier and verification matrix, with unsupported
combinations documented rather than a universal-compatibility claim. Three tiers, stated plainly.

---

## Native self-contained artifacts

Applies to `DevBuddy.Cli`, `DevBuddy.Api`, and `DevBuddy.McpServer`.

### Tier 1 — Verified

Built **and** smoke-tested in CI on every release. The smoke test starts the artifact, checks it
reports its version, and for the API and MCP server checks the health endpoint responds.

| RID | Build | Smoke test | Notes |
|---|---|---|---|
| `linux-x64` | planned | planned | Primary deployment target. |
| `linux-arm64` | planned | planned | |
| `win-x64` | planned | planned | Primary development target. |
| `osx-arm64` | planned | planned | |

### Tier 2 — Built, unverified

Published as-is. **Labelled unverified in every release note.** No automated verification runs
against these, and no support is implied.

| RID | Build | Smoke test | Notes |
|---|---|---|---|
| `win-arm64` | planned | none | |
| `osx-x64` | planned | none | |
| `linux-musl-x64` | planned | none | Alpine and other musl distributions. |
| `linux-musl-arm64` | planned | none | |

### Tier 3 — Not published

Everything else, including every remaining RID in the .NET 10 supported-OS list. Out of scope for
v1 and **not claimed as supported**. Recheck the list at each release:
<https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md>

---

## What self-contained does and does not mean

Stated because `info.md` requires it stated:

- Self-contained **includes** the .NET runtime in the artifact.
- Self-contained does **not** mean a single-file executable. We do not publish single-file.
- Self-contained does **not** mean Native AOT. We do not use Native AOT.
- Self-contained does **not** remove native OS dependencies. See below.

## Native OS dependencies

| Platform | Requirement |
|---|---|
| Linux (glibc) | ICU, unless invariant globalization is enabled. OpenSSL for TLS. |
| Linux (musl) | musl-compatible ICU and OpenSSL packages. |
| Windows | None beyond a supported Windows version. |
| macOS | None beyond a supported macOS version. |

`InvariantGlobalization` is **false** repository-wide. Culture-correct comparison and formatting
matter for a system holding multilingual knowledge records, so ICU is a real requirement, not an
accident of defaults.

---

## Container images

A **separate** matrix, as `info.md` requires. Images do not inherit the native tiers above.

| Platform | Images | Base |
|---|---|---|
| `linux/amd64` | api, mcp, cli | Chiseled ASP.NET 10 / runtime 10 |
| `linux/arm64` | api, mcp, cli | Chiseled ASP.NET 10 / runtime 10 |

Windows and macOS container images are not published.

All images run as a non-root user, mount no Docker socket, and receive secrets through environment
or a secret store rather than image layers. See controls SB-30 to SB-32.

---

## Release checklist

Nothing on this list has been executed yet.

1. Recheck the .NET 10 supported-OS list; adjust tiers if it changed.
2. Build every Tier 1 and Tier 2 RID.
3. Run the smoke test on Tier 1 only.
4. Build and push both container platforms; record image digests.
5. Generate the SBOM and attach it.
6. Run the destroy-and-restore drill (SB-33).
7. Copy every row from `docs/security/verification-matrix.md` that is not `TESTED` into the release
   notes, verbatim.
8. Label Tier 2 artifacts unverified in the release notes.
