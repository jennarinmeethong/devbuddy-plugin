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
| `provider` | The mode, the model, and its dimension. |
| `egress` | Present only for the hosted mode: project text leaves the installation. The hosted mode needs everything below plus the acceptance in step 3. |
| `vector index` | `present`. The database runs `pgvector/pgvector:pg17`, pinned by digest (`deployment.md` has the volume move). |
| `worker token` | Valid, and its owner holds **Viewer** and nothing wider, in one workspace. The account is the sweep's own; a person's token is not used. |
| `budget` | The number of texts per pass the entry approves. |

## 2. Check what the command cannot see

- **Where the model server is**, which decides the mode (`deployment.md` has the table):
  - in the stack: it publishes no port, runs as a non-root user, is read-only, and is pinned by
    digest;
  - on another machine on the LAN (Ollama, LM Studio): which machine, who administers it, and that
    its firewall admits only this installation. `SelfHosted` refuses a public address, but not a
    VPN route to a machine elsewhere, so say plainly that it is on the premises;
  - on a cloud machine: the hosted mode, the cloud provider and the machine, and the TLS proxy that
    checks the key in front of it.
- **Which projects are open to AI.** Only those are embedded. Each one's owner opened it on the
  Projects screen, and any bounded scope is what the entry says.
- **SB-18 stays in force.** With no bounded scope, personal data is redacted from what the sweep
  embeds.
- **The data.** Say plainly what the projects hold, for example "no customer, production or
  personal data" as the devbox entry does.

## 3. Write the `info.md` entry

```markdown
## Confirmed Embeddings on <installation> — <date>

- **Approved:** the <self-hosted | hosted> embedding provider on <installation>,
  reachable <where>. Model <model> (<dimension> dimensions), served by <Ollama | LM Studio | …> on
  <in the stack | machine on the LAN | cloud machine at provider>.
- **Egress:** <none: no project text leaves the <host | LAN> | project text is sent to <host>, at <cloud provider>>.
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

## Retrieval quality

`tools/retrieval/evaluate.sh` measures whether semantic search finds the right record. It uses a
fixed synthetic set of 32 records and 64 questions, in English and Thai, and runs them through the
product: the real sweep, and `search_similar_records` over MCP (Phase 14, C2). `tools/retrieval/README.md`
says how to run it and how to read it. Change a setting on an installation only when a
measurement here supports the change.

Measured on jmhp on 2026-09-24:
- **Set:** `set.json` at hash `bfcf9292e1fa`.
- **Model:** `qwen3-embedding:0.6b`, the devbox's own model files, mounted read-only into an Ollama
  from the image the devbox pins.
- **Query instruction:** none, as shipped.

| Chunk | Questions | recall@1 | recall@5 | MRR |
| --- | --- | --- | --- | --- |
| 3000 (shipped) | all 64 | **0.813** | **0.953** | **0.883** |
| 3000 | English question, English record | 0.938 | 1.0 | 0.95 |
| 3000 | Thai question, Thai record | 1.0 | 1.0 | 1.0 |
| 3000 | English question, Thai record | 0.813 | 1.0 | 0.906 |
| 3000 | Thai question, English record | 0.5 | 0.813 | 0.676 |
| 3000 | the two long records, answer past the first chunk | 0.75 | 1.0 | 0.875 |
| 1500 | all 64 | 0.813 | 0.953 | 0.882 |
| 3000, LM Studio on JMPC | all 64 | 0.813 | 0.953 | 0.882 |

- **Repeatable.** A second run at 3000 gave the same rank for every question.
- **The LM Studio standby gives the same answers** (2026-09-25). It served
  `text-embedding-qwen3-embedding-0.6b` (GGUF Q8_0) on JMPC. 63 of 64 ranks were identical to
  Ollama's; one question moved from third to fourth. It can stand in for the devbox's Ollama without
  a change in search quality. Switching still needs its own `info.md` entry.
- **Chunk size makes no difference on this set.** At 1500, three ranks moved by one place each, all
  outside first place, so recall did not change.
- **The weak case is a Thai question about an English record.** Half of those questions find it
  first, and three fall outside the top five. Every other language pair finds the right record in
  the top five every time. C1 (a query instruction) is the next thing to measure against this.
- **Not measured yet:** a query instruction, which does not exist until C1.

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
