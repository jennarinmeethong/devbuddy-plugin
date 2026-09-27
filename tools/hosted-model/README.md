# A hosted model server of the owner's own

Phase 14 B3. On 2026-09-27 the owner asked for this to be prepared but not used (`info.md`). No
installation points at one. Enabling DevBuddy's `HostedApi` mode on real project data still needs
an acceptance of its own, naming the cloud provider and the machine, because project text then
leaves the network (ADR-0012, `docs/operations/deployment.md`). Nothing here is built or shipped
by CI.

## What it is

Two containers, for a cloud machine with a public name:

- **Ollama**, pinned to the devbox's build. It publishes no port, and its network is internal, so
  it cannot reach the internet either. It runs as uid 1000 on a read-only root.
- **Caddy**, the one way in. It gets a certificate for `EMBED_DOMAIN`, redirects plain HTTP, and
  passes on **only `POST /v1/embeddings` carrying the right bearer key**. A missing or wrong key
  is 401 and never reaches the model. Ollama's own API (`/api/pull`, `/api/delete`,
  `/api/generate`) is 404 even with the key, so the key allows embedding and nothing else. A body
  is capped at 2 MB. Caddy redacts the `Authorization` header in its log and logs no body.

A third service, `pull`, runs once to download the model, because the server itself has no way out.

## Setting it up

The key is generated on the server and written into `.env` without being shown. The owner copies
it into DevBuddy's `docker/.env` as `DEVBUDDY_EMBEDDING_API_KEY`, and into nothing else.

```bash
umask 077
cat > .env <<EOF
EMBED_DOMAIN=embed.example.com
EMBED_TLS=you@example.com
DEVBUDDY_EMBEDDING_API_KEY=$(openssl rand -hex 32)
EOF
docker compose --profile pull run --rm pull
docker compose up -d
DEVBUDDY_EMBEDDING_API_KEY=$(sed -n 's/^DEVBUDDY_EMBEDDING_API_KEY=//p' .env) sh check.sh https://embed.example.com
```

The machine's firewall opens 80 and 443 only. Port 80 is needed for the ACME challenge and
otherwise only redirects.

DevBuddy's side is in `docs/operations/deployment.md`, under *A model server on a cloud machine,
hosted*: `DEVBUDDY_EMBEDDING_PROVIDER=HostedApi`, the endpoint `https://embed.example.com/v1`, and
the host in `OutboundAccess:AllowedHosts` in `compose.override.yaml`.

## `check.sh`

Run from outside, it sends one synthetic sentence and checks that:

- no key and a wrong key are both 401;
- Ollama's own API, a model pull, and a `GET` are all 404 with the key;
- an embedding with the key is 200 with the expected dimensions (1024 for `qwen3-embedding:0.6b`);
- plain HTTP is a 308 redirect and nothing else.

## How it was tested

Only on devrelease, with `EMBED_DOMAIN=localhost`, `EMBED_TLS=internal` and the ports on loopback.
`docs/plan-phase-14.md` has the results under B3. It has not run on a cloud machine, and
DevBuddy's `HostedApi` mode has not been pointed at it.
