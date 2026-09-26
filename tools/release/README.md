# The release checklist

These scripts are the checklist in `docs/operations/release-matrix.md`, under *Verifying a
release*. Until Phase 14 (A1) they lived on the machines that ran them and were copied between
releases with `sed`. Each release now runs them from its own tag.

Every script records a results file. A line starting `ok` or `FAIL` is a check against an expected
value. Any other line is a fact for the release matrix, such as a digest or a timing. A script exits
non-zero if any check failed. Its last line counts the checks that passed and failed.

**No secret is kept here.** Passwords, signing keys and object-store keys for the throwaway stacks
are generated when a script runs, and they are never printed.
`DeploymentTests.no_release_checklist_script_carries_a_secret` fails the build if a script gives a
secret-looking name a literal value.

## The scripts, in the order a release runs them

| Script | When | Where | Arguments |
| --- | --- | --- | --- |
| `setup-and-suite.sh` | Before the tag | devrelease, Linux x64 with Docker and no SDK | `VERSION COMMIT` |
| `stack-drill-tokens.sh` | Before the tag, after the one above | devrelease | `VERSION` |
| `upgrade.sh` | Before the tag | devrelease for amd64; the Ubuntu arm64 guest for arm64 | `VERSION PREVIOUS COMMIT` |
| `post-images.sh` | After the release workflow pushes the images | devrelease for amd64; the arm64 guest or the Mac mini for arm64 | `VERSION WORKDIR [DATABASE_IMAGE] [HELPER_IMAGE]` |
| `smoke.sh` | After the tag, once per archive | Where the RID runs natively, or in a container with a third argument | `ARCHIVE EXPECTED [IMAGE]` |
| `client-smoke.ps1` | After the tag | The Windows on ARM guest for `win-arm64`. It also runs against `win-x64`. | `-Archive -Expected -Work` |
| `stale-sessions.sh` | After an upgrade of any installation | The installation's own host | `[COMPOSE_PROJECT]` |

`lib.sh` holds what the Linux scripts share. It is sourced, not run.
`stale-sessions.sh` is also step 6 of *Upgrading* in `docs/operations/deployment.md`. It only reads,
so it is the one script meant for an installation's own stack.

- **`setup-and-suite.sh`** clones the commit into `$DEVBUDDY_RELEASE_WORK/v<VERSION>/repo`. It runs
  the full .NET suite in the SDK container, against the host's Docker daemon, then the format check,
  the web client's build and suite in `oven/bun:1`, and a self-contained `linux-x64` publish.
- **`stack-drill-tokens.sh`** starts the stack from that clone, from clean, as Compose project
  `devbuddy-v<version>` on `127.0.0.1:18080/18081`. It then runs the destroy-and-restore drill and
  checks that tokens stay out of the log. Last come the checks earlier releases added.
- **`upgrade.sh`** installs the previous release from its published images and its tag's Compose
  file, then seeds data. It upgrades to the commit built from source, as Compose project
  `devbuddy-up<version>` on `127.0.0.1:28080/28081`, and checks that the data came across. The
  script is the same on amd64 and arm64. A host with no `curl` gets one from a container. A plugin
  session is held open across the upgrade, and it has to be listed on the old image and then go
  away when its input closes (A3).
- **`post-images.sh`** starts the three published images with the tag's Compose file, as Compose
  project `devbuddy-rel<version>` on `127.0.0.1:38080/38081`, then removes them. Its
  `results/ai-operations.txt` holds the published console image's AI operation names, in order.
  `smoke.sh` and `client-smoke.ps1` compare every archive against that file.
- **`smoke.sh`** unpacks one archive and checks two things. The console must list those names in
  that order, and it must refuse `retention --every 24` with exit 2. `ubuntu:24.04` covers
  `linux-x64` and `linux-arm64`, and `alpine:3` covers the musl RIDs. With either image, the script
  installs what the platform needs, as the release matrix lists it.
- **`client-smoke.ps1`** checks the `win-arm64` client (`info.md`, 2026-09-24): the console, and
  `DevBuddy.McpServer --stdio` answering `initialize` and `tools/list` with no database.

Checked against a published archive, "identical" means the same names in the same order as the
published `devbuddy-cli` image prints.

**devrelease** is LXC 101 on the Proxmox host, `jm@192.168.1.161` (`ssh devrelease`), since
2026-09-26. It replaced jmhp, which ran these rows until the owner reinstalled it as that host. It
is a Debian 13 unprivileged container with Docker, like the devbox beside it, and holds throwaway
stacks only, never an installation.

## Settings

| Variable | Default | On devrelease |
| --- | --- | --- |
| `DEVBUDDY_RELEASE_WORK` | `~/devbuddy-release` | the default |
| `DEVBUDDY_RELEASE_CACHE` | `$DEVBUDDY_RELEASE_WORK/cache` | the default |
| `DEVBUDDY_SDK_IMAGE` | `mcr.microsoft.com/dotnet/sdk:10.0` | |
| `DEVBUDDY_REPOSITORY_URL` | this repository on GitHub | |
| `DEVBUDDY_REGISTRY` | `ghcr.io/jennarinmeethong/devbuddy-plugin` | |
| `DEVBUDDY_PREVIOUS_EVIDENCE_FROM_SOURCE` | unset | unset. `upgrade.sh` only: `1` builds the previous release's evidence store from the new commit's `docker/evidence`, for a machine that cannot pull `v1.6.0`'s `quay.io` image. The upgrade then no longer proves that the new store reads a volume the old image wrote. Used on arm64 for `v1.7.0` at the owner's decision. |

A script refuses a work directory that already exists. Results from two runs are never mixed.

## Running them for a release

`1.7.0` is the release being cut, `1.6.0` is the one before it, and `$COMMIT` is the commit to be
tagged, pushed to GitHub. The scripts are taken from that commit, so the checklist is the
release's own.

```bash
git clone -q https://github.com/jennarinmeethong/devbuddy-plugin.git /tmp/checklist
git -C /tmp/checklist checkout -q --detach "$COMMIT"
T=/tmp/checklist/tools/release
bash $T/setup-and-suite.sh 1.7.0 "$COMMIT"
bash $T/stack-drill-tokens.sh 1.7.0
bash $T/upgrade.sh 1.7.0 1.6.0 "$COMMIT"
# after the release workflow has pushed the images:
bash $T/post-images.sh 1.7.0 ~/devbuddy-release/post170
sh $T/smoke.sh devbuddy-linux-x64.tar.gz ~/devbuddy-release/post170/results/ai-operations.txt ubuntu:24.04
```

On the Mac mini over SSH, Docker Hub cannot be reached (see the release matrix). Give
`post-images.sh` `public.ecr.aws/docker/library/postgres:17-alpine` and
`public.ecr.aws/docker/library/alpine:3`.

Downloading a published archive onto a machine needs the owner's approval each time. So does
running a script on any machine other than devrelease.

## Adding a check for a release

A release adds lines. It does not change the logic.

- **`stack-drill-tokens.sh`:** write a function below *checks added by a release* and add its name
  to `CHECKS`. The drill runs first and leaves `WS`, `PROJECT`, `ADMIN`, `RECORD` and `HASH` set
  for later checks. Name the release that added the check in a comment above it.
- **`upgrade.sh`:** add the function to `BEFORE_UPGRADE`, `DURING_UPGRADE` or `AFTER_UPGRADE`.
  Checks that describe only the previous release are removed when the next one is cut. An example
  was `leftover_dialect`, which existed because `v1.5.0` had the setting, and went when `v1.7.0`
  was cut. Set `SESSION_SURVIVES` to
  401 for a release that makes everyone sign in again.
- **Figures that come from the checkout, never from the script:** the migration count and the name
  of the last migration are read from the source, so a new migration does not need an edit. The
  AI operation count is 20. It is in the scripts on purpose, because the AI surface does not grow
  without a decision (`info.md`).

Record in the release matrix which script produced each row.
