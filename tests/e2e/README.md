# End-to-end tests

Playwright tests over the whole running system. They drive the web client the API host serves, and
call the HTTP API and the MCP server's HTTP transport directly. Behind those sit real PostgreSQL and
the real object store. Nothing is faked. Every other suite in this repository stops at one host or
at `WebApplicationFactory`. This one starts the images a deployment runs.

## Running it

```bash
bash tests/e2e/run.sh
```

That builds `docker/compose.yaml` with `compose.e2e.yaml` on top and starts it under its own Compose
project, `devbuddy-e2e`. Then it:

1. Bootstraps a workspace with the console.
2. Runs the suite in Playwright's image on the stack's network.
3. Removes the containers and volumes.

Results go to `tests/e2e/.out`:

- `report/index.html`: the HTML report, with a trace, screenshot and video for every failure.
  Open it with `bunx playwright show-report tests/e2e/.out/report`.
- `junit.xml`
- `stack.log`: what the API, the MCP server and `migrate` logged. It is taken before the restore
  stage replaces them, with timestamps, and what the restored servers logged is added after. Until
  Phase 14 (C4) only the second half was kept, so an exception during the suite left no trace.

Arguments after the script go to `playwright test`:

```bash
bash tests/e2e/run.sh --grep lifecycle
bash tests/e2e/run.sh specs/mcp.spec.ts --workers 1
```

`DEVBUDDY_E2E_KEEP=1` leaves the stack up afterwards, so you can inspect it.

You need Docker with Compose 2.24 or later, and bash; Git Bash works on Windows. Nothing from the
repository runs on the host. The .NET build, the client build and the tests each run in a container.

## Security scan

`DEVBUDDY_E2E_ZAP=1` adds two OWASP ZAP scans after the suite, against the same stack (Phase 14,
C4):

- **A baseline scan** of what the API host serves: the web client, and whatever its spider reaches.
  It only reads responses, so it attacks nothing.
- **An API scan** from the API's own OpenAPI document at `/openapi/v1.json`. It sends attacks to
  every route the document names, and it stops after ten minutes.

Both are unauthenticated, so they cover the sign-in page and every route's answer to a caller with
no token. Both are **report-only**. What they find never fails the run. The reports go to `zap/`
under the output directory, as HTML, JSON and Markdown. `zap/status.txt` says whether each scan
finished, because a scan that broke would otherwise look like a clean report.
`zap/baseline-rules.conf` lists every rule the baseline ran with its default level. Once findings
are triaged, that file becomes the rules file that decides which findings fail a run.

CI runs it on the amd64 run only and puts both reports in the job summary. A scan that did not
finish, or never ran, is a warning there, not a failure.

## What the stack differs in

Four things, all in `compose.e2e.yaml`:

- **No host ports.** A run cannot collide with an installation already on 8080 and 8081.
- **A higher sign-in rate limit.** The suite signs in hundreds of times from one address, which the
  limit exists to stop. `DevBuddy.Api.Tests` covers the limit itself.
- **The runner service.** It sits behind the `e2e` profile.
- **ZAP.** It sits behind a `zap` profile of its own, pinned by digest, and only
  `DEVBUDDY_E2E_ZAP=1` starts it.

Everything else is the shipped stack, including the defaults: no embedding provider, no SMTP, and
AI access closed on every project.

## What is covered

| File | What it proves |
| --- | --- |
| `auth.spec.ts` | Sign-in, including one answer for a wrong password and an unknown address, and lockout. Also the session surviving a reload, sign-out, the same recovery answer for every address, and single-use setup tokens. Refresh-token reuse ends the whole chain. |
| `permissions.spec.ts` | The screens each role is offered. The server still refuses what the menu hid, whether reached by URL or by API. Also isolation between workspaces and between projects. |
| `projects.spec.ts` | Creating a project, the AI access switch, deleting a project behind its typed name, and registering a work item. |
| `lifecycle.spec.ts` | A draft written, revised, sent back with a reason, approved at an exact revision, published, read by a viewer and archived, with each step done by the person whose job it is. Also stale-hash approval (SB-23) and steps taken in the wrong state. |
| `content-safety.spec.ts` | A credential in a draft or an attachment is blocked with nothing kept (SB-17). The text check names a rule, never the match, and redaction removes it. |
| `evidence.spec.ts` | Attaching, downloading byte-for-byte and citing evidence from a draft. Evidence is reachable only through an authorised request for its own project. |
| `search.spec.ts` | Full-text search defaults to published records. Semantic search gives a reason instead of an empty list. The review queue and the status filter. |
| `administration.spec.ts` | Invitations, role changes, revocation taking effect on the next request, project-scoped grants, teams, sponsored workspaces, and machine tokens. |
| `operations.spec.ts` | Component health and backup, the quality sweeps, reindex, export, and the audit trail with its channel and without content. |
| `analysis.spec.ts` | The seven analyses, a path escaping the repository, change impact, comparing two tags, and synchronisation. They run over a git working copy the suite writes as files, the way DevBuddy reads them. |
| `mcp.spec.ts` | The tool list equals the manifest's AI allow-list. A project closed to AI is invisible. An assistant's draft is marked and audited as an assistant's whatever it claims. A viewer's assistant cannot write. |
| `host.spec.ts` | The client's route fallback never shadows an API answer. The build has no source maps. No script errors while moving through the screens. |

## After the suite

`run.sh` runs two stages of its own once Playwright finishes, from the host, because neither can be
driven from inside the runner:

- **MCP over stdio** with a machine token, the path the plugins use (`stdio.log`).
- **Destroy and restore** (`restore.log`): a backup of the stack the suite just filled, both the
  database and the evidence volumes destroyed, a restore, and a comparison of accounts, records,
  revisions, evidence rows, one artefact's bytes, and that an access token from before is refused.
  `DEVBUDDY_E2E_RESTORE=0` skips it.

## What is not covered

- **Embeddings and the workers in the default run.** The default stack has no model server and no
  pgvector, so the default run checks only that semantic search explains why it cannot answer. The
  embeddings mode covers them: `DEVBUDDY_E2E_EMBEDDINGS=1 bash tests/e2e/run.sh
  specs/embeddings.spec.ts` runs pgvector, a stand-in model server that keeps what it is sent, and
  the real sweep as a Viewer account. CI runs it as a job of its own.
- **The observability overlay in the default run.** `DEVBUDDY_E2E_OBSERVABILITY=1 bash
  tests/e2e/run.sh specs/observability.spec.ts` covers it: traces in Tempo that carry no identifier
  and no URL path, logs in Loki, and the provisioned Grafana dashboard. CI runs it as a job of its
  own.
- **The GitHub API source mode in the default run.** `DEVBUDDY_E2E_GITHUB=1 bash tests/e2e/run.sh
  specs/github.spec.ts` covers it: a second API instance on the GitHub mode against a stand-in
  GitHub, beside the first on working copies. CI runs it as a job of its own.


## Writing a test

- **Use a project of the test's own.** Take the `project` and `workItem` fixtures in
  `support/fixtures.ts` rather than sharing one, so tests run in parallel without seeing each
  other's records.
- **Create any extra people with `invite`.** Do that for anyone the test will revoke, lock out or
  give another workspace. The shared administrator must keep exactly one workspace, because the
  setup project checks for that.
- **Find elements the way a person does.** Use role and visible name. The client has no test IDs,
  and adding them would test something nobody sees.
- **Build anything a secret scanner would flag at run time**, as `content-safety.spec.ts` does, so
  this directory is not itself a finding.
