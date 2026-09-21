# Approving embeddings on an installation

An installation may run the embedding provider and `record-embedding-sweep` against real project
data only with an `info.md` entry of its own (`info.md`, 2026-09-10 and 2026-09-21). This page is
what that entry is written from. The devbox entry of 2026-09-16 is the worked example.

## 1. Check what the installation shows

Run the check in the sweep's own service, so it sees that service's settings and token:

```bash
docker compose --profile workers run --rm --no-deps record-embedding-sweep embedding-check --budget 50
```

It changes nothing and sends no text. Each line is `ok` or `PROBLEM`, and it exits 3 if any line is
a problem:

| Line | What it must show |
| --- | --- |
| `provider` | The mode, the model, its dimension, and the dialect. |
| `egress` | Present only for the hosted mode: project text leaves the installation. The hosted mode needs everything below plus the acceptance in step 3. |
| `vector index` | `present`. The database runs `pgvector/pgvector:pg17`, pinned by digest (`deployment.md` has the volume move). |
| `worker token` | Valid, and its owner holds **Viewer** and nothing wider, in one workspace. The account is the sweep's own; a person's token is not used. |
| `budget` | The number of texts per pass the entry approves. |

## 2. Check what the command cannot see

- **The model server** publishes no port, runs as a non-root user, is read-only, and is pinned by
  digest (self-hosted mode).
- **Which projects are open to AI.** Only those are embedded. Each one's owner opened it on the
  Projects screen, and any bounded scope is what the entry says.
- **SB-18 stays in force.** With no bounded scope, personal data is redacted from what the sweep
  embeds.
- **The data.** Say plainly what the projects hold, for example "no customer, production or
  personal data" as the devbox entry does.

## 3. Write the `info.md` entry

```markdown
## Confirmed Embeddings on <installation> — <date>

- **Approved:** the <self-hosted | hosted (Voyage AI)> embedding provider on <installation>,
  reachable <where>. Model <model> (<dimension> dimensions).
- **Egress:** <none: no project text leaves the host | project text is sent to api.voyageai.com>.
- **Data:** <what the open projects hold>. <No bounded scope | the bounded scopes named>.
- **What is indexed:** only projects whose owner enabled AI access, and only published revisions,
  in chunks of <n> characters.
- **The worker:** `record-embedding-sweep` as its own account, Viewer in <workspace>, with a token
  that account minted. At most <budget> texts per pass, every <interval>. Revoking the token stops
  the next pass.
- **Key holder (hosted only):** <who set DEVBUDDY_EMBEDDING_API_KEY on the server>.
- **Not approved:** <anything left out, for example the stale-record sweep, a generative model,
  another installation>.
- **`embedding-check` output:** <pasted, with the date it ran>.
```

## Rolling back

1. Revoke the worker's token on the Plugin access screen. The next pass is refused.
2. Set `DEVBUDDY_EMBEDDING_PROVIDER=None` and recreate `api`, `mcp` and the worker.
3. Drop the index rows if the approval is withdrawn, not only paused:

   ```bash
   docker compose exec database psql -U devbuddy -d devbuddy -c 'truncate record_embeddings;'
   ```

   The index is derived, so truncating it loses nothing that was not already elsewhere.
4. Record the withdrawal in `info.md`.

A generative model is not covered by any of this. It would be a new capability, and needs an ADR
first (`info.md`, 2026-09-21).
