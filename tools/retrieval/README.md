# Measuring retrieval

Phase 14, C2. `evaluate.sh` measures whether semantic search finds the right record. A change to
the model, the chunk size or the query instruction (C1) is judged by these numbers. The numbers are
recorded in `docs/operations/embedding-approval.md`, under *Retrieval quality*.

## What it measures

`set.json` is a fixed evaluation set:
- 32 synthetic records about an invented payments product, 16 in English and 16 in Thai;
- 64 questions, each labelled with the record it should find.

Every record has one question in its own language and one in the other. Records come in pairs that
share a topic (retries, caching, migrations, accessibility), so a model has to tell near neighbours
apart. Two long records carry their answer past the 3000-character chunk boundary.

The set holds no project data, no personal data, and nothing shaped like a credential. SB-18 would
redact personal data on the AI channel, and SB-17 would refuse a credential, so either would change
what is measured.

The script runs everything through the product, so chunking, redaction and ranking are part of the
number:
1. It builds a throwaway stack from this checkout, with pgvector, as Compose project
   `devbuddy-retrieval`.
2. It publishes each record over the HTTP API.
3. The real `record-embedding-sweep` embeds the records, as a Viewer account of its own.
4. It asks every question through `search_similar_records`, over the MCP server's stdio transport,
   on the AI channel.

It reports three figures, overall and per language pair, and for the long records:
- **recall@1:** the right record ranked first;
- **recall@5:** the right record in the top five;
- **MRR:** the mean of 1/rank over the top ten, counting 0 when the record is absent.

It also lists every question whose record fell outside the top five.

## Running it

It needs Docker with Compose, `jq`, `openssl` and `curl`. The model server is one of two:

```bash
# The devbox's model, served by an Ollama started here from the image the devbox pins, with the
# devbox's model volume mounted read-only. The devbox's own stack is not touched.
EVAL_OLLAMA_VOLUME=devbuddy_ollama bash tools/retrieval/evaluate.sh /data/devbuddy-cache/c2/run-1

# Any OpenAI-compatible server the containers reach on a private address, such as the LM Studio
# standby on JMPC while its server is running.
EVAL_ENDPOINT=http://192.168.1.111:1234/v1 EVAL_MODEL=text-embedding-qwen3-embedding-0.6b \
  bash tools/retrieval/evaluate.sh /data/devbuddy-cache/c2/run-2
```

| Variable | Default |
| --- | --- |
| `EVAL_MODEL` | `qwen3-embedding:0.6b` |
| `EVAL_DIMENSIONS` | `1024` |
| `EVAL_CHUNK` | `3000`, the shipped `Embedding:ChunkCharacters` |
| `EVAL_QUERY_INSTRUCTION` | none; `Embedding:QueryInstruction` for the run (Phase 14, C1) |
| `EVAL_SET` | `set.json` beside the script |
| `EVAL_KEEP=1` | leave the stack running afterwards |

The work directory must not exist. It keeps `ranks.json` (every question's rank), `mcp.out` (the
raw answers) and `results.txt`. The stack and its volumes are removed at the end.

## Reading the numbers

- A number belongs to one version of `set.json`. The script prints the set's hash, so record the
  hash with the number. Editing the set starts a new series.
- Sixty-four questions is a small sample: one question is 0.016 of recall. A difference of one or
  two questions between two settings is not a finding.
- Embeddings are deterministic for a given model server, so the same settings give the same ranks.
  The first repeat showed this, and it is what makes one run per setting enough.
