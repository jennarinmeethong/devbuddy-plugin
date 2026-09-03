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
| `devbuddy-api` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | `linux/amd64` | Built and run: healthy in the Compose stack, serving sign-in and operations. |
| `devbuddy-mcp` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` | `linux/amd64` | Built and run: refuses an unauthenticated call with 401. |
| `devbuddy-migrate` (console) | `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` | `linux/amd64` | Built and run: applied migrations, bootstrapped, and performed a restore. |

`linux/arm64` images are **not built and not claimed**. The Dockerfiles have nothing
architecture-specific in them and a multi-platform build is a CI change rather than a code one, but
until that build runs the row would be a guess.

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

## Verifying a release

Before publishing anything, and recorded per release:

1. `dotnet test DevBuddy.slnx -c Release` on Linux, with Docker available, so the integration and
   drill tests actually run.
2. `dotnet publish` for every row in the first table. A row that fails to build comes out of the
   table; it does not get a footnote.
3. Run the smoke test on every row whose "Run" column says yes. If a platform cannot be run this
   time, its "Run" column changes to no for that release.
4. `docker compose -f docker/compose.yaml up -d` from clean, to healthy.
5. The destroy-and-restore drill in `docs/operations/backup-and-restore.md`, by hand, once.
6. SBOM and vulnerability scan attached to the release (CI produces both).

Release notes state, verbatim, which rows were unverified.
