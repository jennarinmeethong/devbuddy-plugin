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

The API is then on `127.0.0.1:5010` and the MCP server on `127.0.0.1:5011`. `DEVBUDDY_API_PORT`
and `DEVBUDDY_MCP_PORT` in `docker/.env` change the port; the binding stays on loopback. Until
2026-09-26 they were 8080 and 8081.

## The administration UI

**It is inside the API image.** `docker/Dockerfile.api` builds `web/admin` with Bun and copies the
result into `wwwroot`, so the client and the API it was generated from are one origin, one image
and one deployment. There is no second container, no CORS policy, and nothing for a reverse proxy
to know beyond the one port:

```
devbuddy.example.com {
    reverse_proxy 127.0.0.1:5010
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

**With no domain, an internal CA works.** The devbox has run this way since 2026-09-26: Caddy in
a Compose project of its own beside the stack, on the stack's network, proxying to `api:8080`, with
`tls internal` for an IP address and each device trusting Caddy's root once. Three things cost
time there:

- **A client that connects to an IP address sends no SNI**, browsers included. Without
  `default_sni <address>` in the global options, Caddy has no certificate to choose and ends the
  handshake with an internal error alert, while `openssl s_client`, which sends one anyway, works.
- **The official Caddy image's binary carries the file capability `cap_net_bind_service`.** With
  `cap_drop: [ALL]` and `no-new-privileges`, the kernel refuses to exec it ("operation not
  permitted"), even on unprivileged ports. Add that one capability back.
- **The root lives in Caddy's data directory.** Losing it means every device trusts a new one, so
  back that directory up with the host.

**Behind a proxy, the rate limits see the proxy's address.** The API does not read
`X-Forwarded-For`, so every signed-out caller shares one sign-in limit. On an installation with a
handful of people that is tolerable; raise `RateLimiting:AuthPermitLimit` if it is not.

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
`OutboundAccess:AllowedHosts`, and per `info.md` needs an acceptance of its own before it is used
with real project data. **No vendor is named** since 2026-09-23, when the owner withdrew Voyage AI;
the hosted mode is for an operator's own model server outside the network, below.

Both modes speak the OpenAI-compatible `/embeddings` shape, so the server can be Ollama, LM Studio,
vLLM, llama.cpp or text-embeddings-inference, in the stack or on another machine. **Which mode is
not a matter of taste: it is where the machine is.**

| Where the model server is | Mode | Endpoint, for example |
| --- | --- | --- |
| A service in this Compose stack | `SelfHosted` | `http://ollama:11434/v1` |
| Another machine on the same private network | `SelfHosted` | `http://192.168.1.20:11434/v1` (Ollama), `http://192.168.1.20:1234/v1` (LM Studio) |
| A cloud machine, or anywhere reached over the internet | `HostedApi` | `https://embed.example.com/v1` |

**A self-hosted endpoint on a public address is refused** (2026-09-23). An address literal is refused
at start-up; a name is resolved before every call, and if any address it resolves to is public the
call is refused with nothing sent: the worker logs that pass as failed, with the reason, and the
schedule continues, and a semantic search fails the same way. Text sent to a public
address leaves the installation, and the self-hosted mode is what tells `embedding-check` and the
gateway that nothing does. "Private" is the definition the URL guard uses: loopback, link-local, the
RFC 1918 ranges, `100.64.0.0/10`, and IPv6 unique-local. **What it cannot see** is a VPN: a cloud
machine reached over WireGuard or Tailscale has a private address and would pass. That is the
operator's statement to get right, and such a machine belongs in the hosted mode.

**Another machine on the LAN, self-hosted:**

```bash
DEVBUDDY_EMBEDDING_PROVIDER=SelfHosted
DEVBUDDY_EMBEDDING_ENDPOINT=http://192.168.1.20:11434/v1
DEVBUDDY_EMBEDDING_MODEL=qwen3-embedding:0.6b
DEVBUDDY_EMBEDDING_DIMENSIONS=1024           # must match the model
```

- **Ollama** listens on `127.0.0.1` unless told otherwise: set `OLLAMA_HOST=0.0.0.0` on that
  machine, and let its firewall admit only this installation's host.
- **LM Studio** serves on `localhost:1234` until "serve on local network" is switched on in its
  server settings, and the embedding model has to be loaded there.
- Neither has authentication by default and the traffic is plain HTTP. That is acceptable on a
  LAN the installation already trusts, and it is why the same setup on the internet is not.
- When that machine is off, the worker logs the pass as failed and the next one tries again, and
  a semantic search fails until it is back. Full-text search and everything else are unaffected.

**A model server on a cloud machine, hosted:**

```bash
DEVBUDDY_EMBEDDING_PROVIDER=HostedApi
DEVBUDDY_EMBEDDING_ENDPOINT=https://embed.example.com/v1
DEVBUDDY_EMBEDDING_MODEL=qwen3-embedding:0.6b
DEVBUDDY_EMBEDDING_DIMENSIONS=1024
DEVBUDDY_EMBEDDING_API_KEY=...               # set by the owner, on the server
```

- Put **a reverse proxy with TLS in front of it** (Caddy, for example) that refuses any request
  without the bearer token, and have Ollama or LM Studio listen on `127.0.0.1` only, so the proxy is
  the one way in. Neither checks a key itself, and the hosted mode refuses to start without one.
  Exposing port 11434 or 1234 directly would let anybody embed with it, and carry project text in
  clear.
- The text is on a machine at a cloud provider. The acceptance names that provider and the machine.

The allow-list entry is not a `docker/.env` variable, on purpose. Add it to the `api`, `mcp` and
`record-embedding-sweep` services in `docker/compose.override.yaml`, as
`DEVBUDDY_OutboundAccess__AllowedHosts__0: embed.example.com`, so that enabling egress is a file
somebody wrote rather than a value somebody pasted. **The owner sets the key**, in `docker/.env`
or a secret store, and it is never written anywhere else. The adapter sends it as a bearer header
and never logs it or puts it in an error. A server's refusal is reported by status code alone,
because a server that echoes its input would put project text in the message. Changing model or
dimension re-embeds every record, since vectors from two models are never compared.

**A query instruction, optional (Phase 14, C1).** `DEVBUDDY_EMBEDDING_QUERY_INSTRUCTION` is empty by
default. Set, it tells the model the task a search query is for, in the form Qwen3-Embedding was
trained on (`Instruct: <task>` then `Query: <text>`). Documents never carry it, so setting it,
changing it or clearing it re-embeds nothing. It must be one line. It is scanned with every query
like the query itself. Set it only if `tools/retrieval/evaluate.sh` shows that it helps, and
recreate `api` and `mcp` afterwards. `docs/operations/embedding-approval.md` records what was
measured.

`DEVBUDDY_EMBEDDING_DIALECT` is gone with Voyage. An installation that still sets it is unaffected:
nothing reads it.

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

**The second leaves no vector index on an installation that migrated without pgvector.** The index
migrations are conditional: on a database without the extension they skip their work, and are
still recorded as applied. After a chown move, `migrate` finds nothing pending, the table is never
created, and `embedding-check` reports the vector index as absent. For such an installation, which
is any that ran `migrate` on `postgres:17-alpine`, take the first way: back up, give the database a
fresh volume on the pgvector image, let `migrate` run every migration there, then restore.

```bash
docker compose run --rm --no-deps -T retention backup --workspace <workspace> --actor <administrator>
# Point the database service at a new volume, for example in compose.override.yaml:
#   database:
#     volumes: !override
#       - database_pgvector:/var/lib/postgresql/data
docker compose up -d --wait database evidence
docker compose run --rm --no-deps -T migrate
docker compose run --rm --no-deps -T migrate restore --reference <reference>
docker compose up -d
```

Accounts, passwords and machine tokens come back with the restore. Sessions do not, so everyone
signs in once. The old volume is left as it was, which makes rolling back a matter of undoing the
override and the image. The LXC devbox was moved this way on 2026-09-26.

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

Raise the request limit if a reverse proxy collapses many people onto one address. The limiter
sees the proxy's own address, not what the proxy tells it: the API reads no `X-Forwarded-For`.
Until 2026-09-26 this line said the opposite.

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
6. List the assistant sessions still on the old image, and restart them from their clients:

   ```bash
   sh tools/release/stale-sessions.sh
   ```

   A plugin over stdio runs one `mcp` container per session, started with `docker compose run`.
   That container keeps the image it started with until the session closes, and `up -d` does not
   touch it. A new session gets the new image. The script lists the sessions whose image differs
   from the running `mcp` service's, ends with a count, and changes nothing.
   `docs/operations/plugin-hosts.md` says why stopping those containers is the wrong fix. Record
   the count for an upgrade you document.

A migration that fails leaves the servers not started rather than started against a schema they do
not match.

**Once, when upgrading past 2026-09-26: the host ports moved.** The API is published on
`127.0.0.1:5010` rather than 8080, the MCP server on 5011 rather than 8081, and Grafana on 5012
rather than 3000. A reverse proxy, a firewall rule or a bookmark that names the old port stops
reaching the stack. Point it at the new port, or keep the old one by setting `DEVBUDDY_API_PORT=8080`
and `DEVBUDDY_MCP_PORT=8081` in `docker/.env`. An override that replaces `ports:` itself, as an
installation publishing on the LAN does, is unaffected. The plugin over stdio uses no port.

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
