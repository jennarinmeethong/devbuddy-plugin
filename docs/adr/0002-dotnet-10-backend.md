# ADR-0002: .NET 10 for core, console, API, and MCP server

- Status: Accepted
- Date: 2026-09-01
- Phase: 0

## Context

`info.md` states .NET 10 for the shared core, console application, web API, MCP server, and other
product-owned backend tools.

## Decision

Target `net10.0` across every backend project. Pin the SDK in `global.json` with
`rollForward: latestFeature`. Apply repository-wide settings in `Directory.Build.props`:
nullable reference types enabled, implicit usings enabled, warnings treated as errors, code style
enforced in build, and the latest recommended analysis level. Manage every package version
centrally in `Directory.Packages.props` with transitive pinning enabled.

## Consequences

- Warnings-as-errors is set in Phase 0, before there is any code, so it never has to be retrofitted
  against a backlog of existing warnings.
- Central package management gives control SB-28 a single place where a dependency version can
  change, which is what makes supply-chain review tractable.
- Cost: a new analyzer release can break the build on upgrade. Acceptable, and preferable to
  warnings nobody reads.

## Alternatives considered

- **Warnings as warnings.** Rejected: in practice the count only grows.
- **Per-project package versions.** Rejected: makes SB-28 unverifiable.
