# Deployment

Self-hosted, on infrastructure you control. Four containers: PostgreSQL, MinIO, the API, and the
MCP server, plus a console that runs once and exits.

## Starting

```bash
cp docker/.env.example docker/.env
# Fill in the four secrets. Generate the long ones with something actually random:
#   openssl rand -base64 48

docker compose -f docker/compose.yaml up -d
docker compose -f docker/compose.yaml run --rm migrate \
  bootstrap --workspace-name "Your workspace" --email you@example.com --password '...' --project-name "First project"
```

The bootstrap prints the workspace, project, and your user identifier. It runs once and refuses
afterwards.

The API is then on `127.0.0.1:8080` and the MCP server on `127.0.0.1:8081`.

## The administration UI

**It is inside the API image.** `docker/Dockerfile.api` builds `web/admin` with Bun and copies the
result into `wwwroot`, so the client and the API it was generated from are one origin, one image
and one deployment. There is no second container, no CORS policy, and nothing for a reverse proxy
to know beyond the one port:

```
devbuddy.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

Paths that belong to the client — `/`, `/set-password`, and everything under `/w/` — are served
`index.html` and routed in the browser. Everything the API maps keeps its own answer, refusals
included, because the fallback only sees what no endpoint matched. A GET of a path that exists for
POST is in that category: `GET /operations/anything` serves the client, because operations are
invoked with POST and nothing maps that address for a GET.

Two consequences worth knowing. Assets carry a content hash and are served `immutable`, while
`index.html` is served `no-cache`, so a deployment lands without anybody clearing anything. And a
host with no `wwwroot` — which is what running from source is — skips all of it and serves the API
alone, rather than guessing at a build that is not there.

## What the stack does and does not do

**It does not terminate TLS.** There is no certificate in it and no self-signed one pretending to
be a certificate. Put a reverse proxy in front — Caddy, nginx, whatever you already run — and let
that hold it. Both application ports are bound to loopback specifically so that the proxy is the
only way in.

**The database and the object store publish no ports at all.** They are reachable on the internal
network and nowhere else. If you need a `psql` session, run it inside the network:

```bash
docker compose -f docker/compose.yaml exec database psql -U devbuddy devbuddy
```

**Nothing mounts the Docker socket.** A container that can reach the daemon is root on the host.

**Nothing runs as root.** The application images ship a non-root account and use it, and the
database is told which user to be. MinIO is built from `docker/evidence/Dockerfile`, which adds the
non-root account (uid 1000) that the upstream image lacks, and keeps its data at `/srv/evidence`.
Until 2026-09-14 this paragraph claimed MinIO already ran as its own account, and in fact it ran as
root; `DeploymentTests` now checks every service. An installation upgrading from an earlier stack
has to hand its evidence volume to that account once; the upgrade steps are below.

**The application containers are read-only** with all capabilities dropped and
`no-new-privileges`, with a `tmpfs` for the one directory a .NET process needs to write to.

**It applies the retention schedule on its own.** The `retention` service runs
`retention --every 24h` in the console image — the same sweep as `dotnet run -- retention`, which
purges audit events past their window, orphaned evidence, stale backups and exports, and rolled log
files. Through v1 this service did not exist and the sweep ran only if an operator built something
to run it, so the windows in `docs/adr/0009-retention-defaults.md` were enforced in code and
unscheduled in fact.

`DEVBUDDY_RETENTION_EVERY` changes the interval; a minute is the shortest accepted, and daily is
enough because every window in the schedule is measured in days or years. The service runs
**outside** the use-case pipeline and holds no credential of any kind: a sweep spans every
workspace and project, so there is no caller to authorise it against, and a scheduler with one
would be an installation-wide superuser running unattended. Expect its first pass on start, and
expect zeroes on a young installation.

**It can run background workers, and does not unless asked.** `stale-record-sweep` and
`record-embedding-sweep` are services in `docker/compose.yaml` behind a `workers` profile, so a
plain `up -d` starts neither:

```bash
docker compose -f docker/compose.yaml --profile workers up -d
```

Unlike `retention`, a worker runs **inside** the pipeline, as somebody. Each reads a machine token
from `docker/.env` — `DEVBUDDY_STALE_SWEEP_TOKEN` and `DEVBUDDY_EMBEDDING_SWEEP_TOKEN` — minted in
the web interface for an account whose membership is what that job needs, in the one workspace it
works in. Every call the job makes is authorised against that membership and audited under that
account's name. The token is resolved again on every pass, so **revoking it stops the next pass**
without touching this file or restarting anything. Use one account per job:

- **`stale-record-sweep`** calls `detect_staleness`, which needs `ManageIndex`. Give its account
  the **`IndexMaintainer`** role, which carries reading, its own credentials and `ManageIndex` and
  nothing else, on one workspace, not more. Until Phase 13 only Administrator carried
  `ManageIndex`, so this worker's token had an administrator's reach; do not go back to that. It
  sends nothing to a model. `DEVBUDDY_STALE_AFTER` has no default and the worker refuses to start without it, because
  how long untouched is suspect depends on the installation.
- **`record-embedding-sweep`** needs read access, which Viewer carries, and nothing more. It also
  needs an embedding provider configured, a pgvector-capable database image, and at least one
  project whose owner enabled AI access: a project nobody opened to AI is never listed to it, so it
  is never read and never embedded. It embeds published revisions only.
  `DEVBUDDY_EMBEDDING_SWEEP_BUDGET` is the most record texts one pass may send, and its default of
  zero sends nothing — starting the profile spends nothing until that number is written down.

A pass that cannot run — a revoked token, no provider, no pgvector — is logged as **refused** with
the reason, and the schedule carries on to the next pass. A refusal is not an error, and a worker
that logs one every night is telling you something is not configured rather than that something
broke.

**Enabling a provider is a decision, not a setting.** A self-hosted model sends no project text out
of the deployment. The hosted mode does, refuses to start unless its host is on
`OutboundAccess:AllowedHosts`, and per `info.md` needs the vendor named and an acceptance of its
own before it is used with real project data.

**The named hosted vendor is Voyage AI** (`info.md`, 2026-09-21). The adapter is built and tested
against a double shaped like its API; it has not been enabled on any installation. When an
installation's own acceptance is in `info.md`, these are the settings:

```bash
DEVBUDDY_EMBEDDING_PROVIDER=HostedApi
DEVBUDDY_EMBEDDING_DIALECT=VoyageAi          # sends input_type: document / query
DEVBUDDY_EMBEDDING_ENDPOINT=https://api.voyageai.com/v1
DEVBUDDY_EMBEDDING_MODEL=voyage-3.5          # or the model the acceptance names
DEVBUDDY_EMBEDDING_DIMENSIONS=1024           # must match the model
DEVBUDDY_OutboundAccess__AllowedHosts__0=api.voyageai.com
```

The API key is `DEVBUDDY_Embedding__ApiKey`. **The owner sets it on the server**, in `docker/.env`
or a secret store, and it is never written anywhere else. The adapter sends it as a bearer header
and never logs it or puts it in an error. A vendor refusal is reported by status code alone,
because a vendor that echoes its input would put project text in the message. Changing model or
dimension re-embeds every record, since vectors from two models are never compared.

**Moving the database to `DEVBUDDY_DB_IMAGE=pgvector/pgvector:pg17` is a data-directory change, and
not only in name.** Back up first. The pgvector image runs PostgreSQL as uid 999 and the default
Alpine image runs it as uid 70, so the new image cannot open a volume the old one created: the
database restart-loops with `initdb: could not access directory "/var/lib/postgresql/data":
Permission denied`, and every service that waits on it stays in `Created`. Two ways through:
restore the backup into a fresh volume, or stop the database and change the volume's owner before
starting it on the new image:

```bash
docker compose -f docker/compose.yaml stop database
docker run --rm -v devbuddy_database:/d alpine chown -R 999:999 /d
docker compose -f docker/compose.yaml up -d
```

The second keeps the data in place. It was how the owner's test installation was moved on
2026-09-13, after the first attempt restart-looped exactly as described.

**Upgrading a stack from before the non-root evidence store takes one ownership change.** Until
2026-09-14 MinIO ran as root and wrote its volume as root. The evidence service is now built from
`docker/evidence/Dockerfile` and runs as uid 1000, so the new image cannot write to a volume the old
one filled. MinIO then refuses to start with `file access denied, drive may be faulty`, and the API,
which waits on it, never becomes healthy. The volume is the same one, now mounted at
`/srv/evidence`. Hand it to the new account once, before the first start:

```bash
docker compose -f docker/compose.yaml stop evidence
docker run --rm -v devbuddy_evidence:/d alpine chown -R 1000:1000 /d
docker compose -f docker/compose.yaml up -d --build
```

A fresh install needs none of this. The image creates `/srv/evidence` owned by uid 1000, and a new
volume takes that ownership.

**It collects no traces or metrics on its own.** `Telemetry:Endpoint` is unset, so the
instrumentation registers nothing. Adding a second file turns it on together with somewhere to
send it:

```bash
docker compose -f docker/compose.yaml -f docker/compose.observability.yaml up -d
```

That publishes Grafana on loopback and needs `DEVBUDDY_GRAFANA_PASSWORD` set; nothing else in the
overlay is published. `docs/operations/observability.md` covers what it collects and, more to the
point here, what it refuses to.

## Secrets

Four, all from the environment, none baked into an image:

| | |
|---|---|
| `DEVBUDDY_DB_PASSWORD` | PostgreSQL. |
| `DEVBUDDY_SIGNING_KEY` | Signs access tokens. Shared by the API and the MCP server, so one identity works across both. At least 32 characters; a shorter one is refused rather than padded. |
| `DEVBUDDY_EVIDENCE_ACCESS_KEY` / `_SECRET_KEY` | MinIO. |

`docker/.env` is refused by `.gitignore` and a test checks that it still is. For anything beyond a
single host, put these in whatever secret store you already have and inject them; Compose reads the
environment either way.

**Rotating the signing key signs everyone out.** Access tokens are signed and not stored, which is
what makes a fifteen-minute lifetime the thing that bounds them; changing the key invalidates every
one immediately. That is the right lever in an incident and an unpleasant surprise otherwise.

## Volumes

Three named volumes: `database`, `evidence`, `backups`. They survive `docker compose down` and are
removed by `docker compose down -v`, which is the difference between replacing a container and
losing an installation.

`backups` is a volume on the same host, which is enough to survive a container replacement and not
enough to survive losing the machine. **Copy backups off the host.** See
[backup-and-restore.md](backup-and-restore.md).

## Working copies

Analysis reads a mounted working copy: one directory per project, named by project identifier,
under `DEVBUDDY_PROJECTS_PATH`. It is mounted **read-only**, in the Compose file as well as in the
code — DevBuddy never fetches a checkout itself, because cloning means running git and running
anything is what control SB-04 forbids.

```
projects/
  03b3ea15-0330-4a99-92e4-9ed281209713/    <- the project identifier
    <a working copy>
```

A project with no directory is simply not analysable, which is the right answer rather than an
error.

## Availability limits

Configurable, with defaults that suit a self-hosted installation:

| Setting | Default | What it bounds |
|---|---|---|
| `RateLimiting:AuthPermitLimit` | 10 per minute, per address | Credential spraying (SB-13). |
| `RateLimiting:RequestPermitLimit` | 600 per minute, per caller | Everything else. Per account when signed in, per address when not, so one runaway script does not slow everybody down. |
| `Limits:MaxRequestBytes` | 32 MB | The body a process will hold. |
| `Evidence:MaxObjectBytes` | 100 MB | What the evidence store will keep. |
| `Analysis:Timeout` | 60 s | One analysis (SB-22). |
| `Analysis:MaxConcurrentAnalyses` | 4 | Analyses at once, process-wide. Over that, callers wait `Analysis:QueueTimeout` and are then told the system is busy. |

Raise the request limit if a reverse proxy collapses many people onto one address — the limiter
sees what the proxy tells it.

## Health

`GET /health` is liveness only and anonymous: it says the process is up and nothing about what it
can reach. Component health names the database and the evidence store and is an **authorised
operation**, because naming components is a disclosure and a health endpoint is one of the easiest
places to leak a connection string.

The container health check runs the API executable with `--health-check`. A chiseled image has no
curl and no shell, which is the point of one.

## Upgrading

1. Take a backup, and check it is where you think it is.
2. Pull or rebuild the images.
3. `docker compose up -d`. The `migrate` service runs first and the servers wait for it to
   complete, so a deployment cannot race a schema change.
4. Check `/health` and, once, sign in.
5. Check the `retention` service came back up and logged a pass. It is the one service whose
   failure is invisible from the outside: nothing stops working, the windows just stop being
   applied.

A migration that fails leaves the servers not started rather than started against a schema they do
not match.

**Once, when upgrading past 2026-09-17: run `scope-report`.** Until then a workspace administrator
could store a work item, a record, evidence, a project grant or an AI policy against a project
identifier that is not a project of their workspace: another tenant's, a deleted one, or one that
never existed. Nobody else could read those rows, and the authorization service refuses such a
scope now. The console lists any that remain and changes nothing:

```bash
docker compose -f docker/compose.yaml run --rm migrate scope-report
```

It prints one line per table, workspace and project, and exits 3 if it found any. What to do with
them is the operator's call. Audit events are not examined, because they outlive the project they
describe on purpose.

## Audit entries with no channel

Every audit entry written since `v1.3.0` records the channel its request arrived on. Entries
written before an installation upgraded to `v1.3.0` have none. Where they are:

- **An installation that began on `v1.3.0` or later** has none at all.
- **One that upgraded** has them from its first day up to the moment `migrate` applied
  `AuditEventChannel`. That is when the stack first started on `v1.3.0`.

They are **not** backfilled, and must never be (`info.md`, 2026-09-15). Null means "not recorded".
A guess would make the trail less trustworthy than the gap does.

- **Matching:** they match no channel filter.
- **Web UI:** the Audit screen shows them as not recorded, and its channel list has "Channel not
  recorded" to list exactly those.
- **API:** `read_audit_history` with `channelNotRecorded: true` does the same. It is refused
  together with a channel, because the two contradict each other.
