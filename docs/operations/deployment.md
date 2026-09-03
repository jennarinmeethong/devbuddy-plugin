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

**Nothing runs as root.** The application images ship a non-root account and use it; the database
is told which user to be; MinIO uses its own and is left alone, because overriding it leaves the
image unable to write to a fresh volume.

**The application containers are read-only** with all capabilities dropped and
`no-new-privileges`, with a `tmpfs` for the one directory a .NET process needs to write to.

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

A migration that fails leaves the servers not started rather than started against a schema they do
not match.
