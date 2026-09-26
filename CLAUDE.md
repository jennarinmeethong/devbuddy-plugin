# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this
repository.

## Read these first

- `AGENTS.md` — structure, commands, style, testing, and security expectations. It is the primary
  contributor guide and applies here in full.
- `info.md` — decisions confirmed by the project owner. Treat as binding. When the owner confirms
  something new, add it there.
- `docs/plan-phase-14.md` — Phase 14, approved 2026-09-24, and its progress log. Update it the
  same way. The release checklist is scripted in `tools/release/` since its A1.
- `docs/plan-phase-13.md` — Phase 13, approved 2026-09-21, and its progress log. Update the item's
  status and add a dated log entry every time an item is finished.
- `docs/plan.md` — the phased plan, Phase 0 to Phase 12, each with exit criteria. Phase 12 is
  approved as of 2026-09-10, and as of 2026-09-13 all three tracks are done: ADR-0012 and ADR-0013
  are confirmed (0013 amended the same day), the embedding provider is a port with two modes off by
  default, and the worker, two jobs, the embedding adapter, the derived vector index,
  `search_similar_records` and the schedule that runs both jobs are built. SB-34 covers the
  embedding egress path. The self-hosted mode has run end to end on the owner's test installation
  with synthetic data; the hosted mode has never been enabled anywhere.

## Where the project is

**v1 is released, and Phase 12 followed it.** Phases 0 to 11 are complete, the v1 gaps named at the end of Phase 11 are
closed, and `v1.0.0` is published from `9a8ebf0` — signed, an SBOM per image, and the attestations
verified from outside the workflow that built them. Phase 1 delivered `DevBuddy.Domain`; Phase 2
the `UseCaseExecutor` pipeline and the first 41 of what is now 65 operations; Phase 3 PostgreSQL,
full-text search, and MinIO; Phase 4 identity, authorization, and tenant isolation; Phase 5 the
lifecycle and audit history; Phase 6 read-only analysis, the real secret scanner and redactor, the
path and URL guards, and source synchronisation from a mounted working copy; Phase 7 the three
hosts — the HTTP API, the MCP server over stdio and authenticated HTTP, and the console; Phase 8
the provisioning operations and the React administration UI in `web/admin`; Phase 9 machine tokens
and the Claude and Codex plugin packages; Phase 10 the container images, the Compose stack, backup
and restore, and the supply-chain checks; Phase 11 the personal-data policy and retention
enforcement. 993 .NET tests and 78 web tests exist. The .NET suite passed at 993 in CI on
2026-09-26, run 36238723460 on `f321f82`. The web suite passed at 78 on the Windows development
machine the same day. **CI does not run the web suite**: the API image runs `bun run build`,
which type-checks, and no workflow runs `bun test`. The owner's Linux test machine that ran both
suites until 2026-09-25 is gone, above. Count them rather than trusting this sentence, which has been stale many times
already: it sat at the release figure of 433 and 31 while both grew, at 495 and 36 through Phase 12,
and at 624 and 36 until the worker schedule landed, at 655 and 36 until the audit channel landed, at 672 and 36 until the 2026-09-16 merge, at 861 and 47 until the draft editor landed, at 867 and 57 until the audit reference fix landed, at 870 and 57 until archived records left semantic search, at 878 and 57 until every operation got a screen, at 887 and 72 until the evidence bucket race was fixed, at 888 and 72 until the project in a scope was checked and the session refresh stopped unmounting the screen, at 896 and 73 until Phase 13 and the Voyage withdrawal, and at 958 and 78 until the release checklist's secret guard (Phase 14, A1), which passed at 959 on jmhp on 2026-09-24, and at 959 and 78 until the query instruction (Phase 14, C1), which passed at 968 on jmhp on 2026-09-25, and at 968 and 78 until ZAP's fixes (Phase 14, C4) and the email fix. `docs/plan.md` keeps the per-phase figures, and
the ones under *v1 is released* are what passed at `v1.0.0`; they are a record and are not updated.
All 34 controls are `TESTED`. SB-29 closed on that publication; SB-34, the embedding egress path
ADR-0012 required a control for, closed on 2026-09-13.

**`v1.7.0` is the current release**, published 2026-09-25 from `b126c27` at the owner's
approval. It builds MinIO from source, and adds the optional query instruction (C1) and
`tools/release/`. It adds no migration, and nobody signs in again. It is the first release checked
by `tools/release/`, and its checklist is in `docs/operations/release-matrix.md`. The devbox ran
that tag, with Qwen3's published query instruction, until the owner reinstalled that machine the
same day. The tag now runs in an LXC there, below.

**The devbox is an LXC since 2026-09-25.** The owner reinstalled jmhp as Proxmox, and DevBuddy now
runs in an unprivileged LXC on it, with `nesting=1,keyctl=1` so Docker can run inside. It is
`v1.7.0` from the tag's own Compose file, with the API and web UI on the LAN over plain HTTP and
MCP on loopback. **It was installed from clean, so nothing came across:** no records, no vector
index, no Ollama, and no worker accounts or tokens. **Since 2026-09-26 it runs the embedding
provider and both workers**, on the devbox's old terms (`info.md`, same day): Ollama serving
`qwen3-embedding:0.6b` inside the stack, pgvector, Qwen3's query instruction, and each worker as an
account of its own with no password.
The plugin reaches it the way it reached the old machine: SSH with a key restricted to one command,
which starts the MCP server over stdio inside the stack, with the machine token kept on the server.
The SDK-container wrapper the suites ran in on jmhp went with the reinstall; CI still runs them.

**`v1.6.0` was the release before it**, published 2026-09-24 from `c9a0d4d` at the owner's
instruction. It withdraws Voyage AI and holds `SelfHosted` to a private address. It adds no
migration, and nobody signs in again.

**`v1.5.0` was the release before it**, published 2026-09-22 from `cb818a3` at the owner's
instruction. It carries the rest of Phase 13 and one conditional migration, `RecordEmbeddingChunks`.
**Everyone signs in once after upgrading to it**, because an access token from before carries no
session.

**`v1.4.0` was published the same morning** from `163243f` (Phase 13, A1). It carries a screen for
every operation, `list_source_repositories`, the project-scope check and `scope-report`, and adds no
migration.

**`v1.3.0` was the release before it**, published 2026-09-17 from `c850275` at the owner's
instruction. It carries the 2026-09-15 plugin test round's fixes, the audit channel column, the
Thai SB-18 rules and the draft editor. Its source was verified at `84ee6d5`, and `c850275` adds
documentation only. **Upgrading from `v1.2.1` needs no manual step.** Its checklist in
`docs/operations/release-matrix.md` ran `win-x64` natively. `linux-arm64` ran in Docker Desktop's
Linux VM before publication, and natively in the Ubuntu arm64 guest the same day, after it.

**`v1.1.0` is published too (2026-09-10, from `4253a5b`), then `v1.2.0` (2026-09-14, from `7512240`),
and `v1.2.1`**, published 2026-09-14 from `56a4c2a`. `v1.2.1` is the
non-root evidence store and nothing else. **An installation upgrading to it has to `chown` its
evidence volume once** (`docs/operations/deployment.md`). `v1.2.0` was decided on 2026-09-13 in
`info.md`. It carries Phase 12C, the MinIO move to
`quay.io` and dropping `osx-x64`; the Compose file of both published tags names a MinIO image Docker
Hub no longer serves. `v1.2.0-rc.1` proved the changed `release.yml` first, and the full checklist
ran for the tag. The results are in `docs/operations/release-matrix.md`:
- the drill, with both volumes destroyed;
- five smoke tests, `osx-arm64` included;
- `linux-arm64` and all three images on arm64 hardware for the first time.

`v1.2.0`'s release notes say what was run for the two unverified RIDs, and so do `v1.2.1`'s. Both
were published on 2026-09-14 at the owner's instruction. `v1.2.1` was Latest until `v1.3.0`.
Its checklist, in `docs/operations/release-matrix.md`, records that Smart App Control blocked the
unsigned `win-x64` build on the development machine. That row was run under x64 emulation on
Windows on ARM instead, not natively.

What v1 did **not** claim was four platforms built but never run, no `linux/arm64` image, and the
operator-side facts about application logs. Phase 12 closed two of those three: the arm64 images
are built and started, and the log facts are decided rather than deferred — the sweep is scheduled
and tokens are no longer written to a log by default. Two unverified platforms remain, as an
accepted decision rather than a gap, each started once since: on 2026-09-13 `osx-x64` was dropped and `osx-arm64` moved to
the verified tier. `docs/security/release-readiness.md` is the current statement
of what is accepted and by whom.

**Not every operation a person needs is reachable from `web/admin`.** This file said it was, and
on 2026-09-15 a draft created through the plugin could not be moved out of Draft from any screen:
nothing called `submit_for_approval`. The record page now carries the whole lifecycle — submit a
draft (`CreateDraft`), approve or send back (`ReviewRecord`), publish (`PublishRecord`), archive
behind a confirmation (`ArchiveRecord`) — and shows the unpublished revision beside the published
one, because `get_record` with no revision answers the published one and a reviewer was being
shown that body above an approval binding the newer revision's hash.

**Since 2026-09-16 a draft can also be revised there, and a reviewer's reason for sending one back
is shown.**
- **What made it possible:** `get_record` returns the revision's front matter and evidence
  references, redacted like its other text, and `view_record_history` returns every correction
  with its reason.
- **How the editor works:** it reads the newest revision by number and sends the front matter and
  evidence back with the new text. A revision stores exactly what the request carries, so an editor
  that forgot either would drop it silently. The front matter is also part of the hash an approval
  binds, which is why the page shows it.

**Since 2026-09-17 every operation a person needs has a screen** (`info.md`, same day). The owner
decided it; until then six groups were deliberately left to the console.
- **A work item page** carries the work, its records, the form a person writes a new draft with,
  and a handover with open questions and missing evidence. A draft belongs to a work item, which
  is why the form is there and not on the record list.
- **Search** has full-text search, Published by default, and semantic search, which shows the
  server's reason when it cannot answer.
- **Analysis** lists the repositories the project can read, and runs the seven analyses, change
  impact, a comparison of two references and a synchronisation. It needs
  **`list_source_repositories`**, added the same day and human-only. Nothing persists a
  `SourceRepository`, so it reports what the configuration already makes reachable: the GitHub
  mode's entries for the project, or the identifier-named directories under its working copy root.
  No server path is returned.
- **Maintenance** has the quality sweeps, `reindex`, `detect_secrets`, `redact_sensitive_data`
  and `export_project`, each shown only to a role carrying its permission. `backup_system` is on
  Health.
- **Members** changes a role by revoking the grant and then granting the new role on the same
  scope, because a grant's role cannot be edited. Revoking first is the direction that fails safe,
  and it is not offered on the caller's own grant. It can also give somebody already here another
  grant.

The AI surface did not move: it is still twenty operations. The console's `run` still reaches
everything.
Team administration is the
`Teams` screen, standing up another workspace is the `Workspaces` screen (both gated on the
permission, so a viewer is offered neither), and deleting a project is on the project list —
behind typing the project's name back, because it takes records, their history, and the evidence
bytes with it and there is no undo. Adding somebody to a team picks them from the workspace's own
members rather than asking for an identifier to be typed.

**The API host serves that UI, from inside its own image.** `docker/Dockerfile.api` builds
`web/admin` with Bun in a stage of its own and copies `dist` into `wwwroot`, so the client and the
API it is generated from are one origin, one image, and one thing to deploy — a reverse proxy in
front of the stack now needs `reverse_proxy 127.0.0.1:5010` and nothing else. A static-serving
container was the alternative and was refused: `nginx` or `caddy` would put a shell and a package
manager into a stack whose images are chiseled so that there is nothing in them to execute. The
client therefore calls the API **at the root**, not under a prefix, `vite.config.ts` proxies the
API's own top-level routes in development so the dev server looks like the host that will serve it,
and source maps are off because these files are public. A host with **no `wwwroot` serves the API
alone**, which is what running from source does, and `AdminUiTests` covers that case alongside the
one that matters more: the fallback for the client's routes must never put an HTML page in front of
an endpoint that answered with JSON, a refusal, or a 404.

**Source synchronisation can now read the GitHub API, opt-in.** `GitHubOptions.Mode` defaults to
`WorkingCopy` — the mounted-checkout reader Phase 6 shipped, unchanged. Setting it to `GitHubApi`
(a token, and `api.github.com` added to `OutboundAccess:AllowedHosts` — the empty-allow-list
default is not weakened by this existing) swaps in a client that reads pull requests, issues, and
review comments live, and answers `analyze_change_impact` against a commit in a pack file the same
way a checkout does. `sync_sources` reports open pull request and issue counts when the active
client can answer that; a working-copy-backed installation still reports them as absent, honestly,
not as zero.

**SB-18, the personal-data policy, is denied by default on the AI channel and nowhere else.**
Customer, production, and personal data are blocked from a draft and redacted from a read whenever
the caller is on the AI channel and the project's AI access policy carries no approved bounded
scope. A human is never subject to it — SB-18 is an AI data policy, not a general content
restriction — and a secret is still refused even inside an approved scope, per `info.md`.
**Since Phase 13 (B9) a scope names rules.** `enable_project_ai_access` takes
`allowedPersonalDataRules` (names from `PersonalDataRuleNames.All`, which a test holds equal to the
scanner's rules) and a required justification. Only those rules are let through on the AI channel;
every other one is still blocked and redacted. The old free-text form is refused for new approvals;
a stored one is still read as switching every rule off, and the Projects screen says so.

**SB-27, retention, is enforced for audit events, evidence, backups, and exports.**
`dotnet run -- retention` is a console command, outside the pipeline for the same reason `restore`
is; since Phase 12B the stack runs it on a schedule, below. `export_project` now writes an
actual copy — records, work items, and evidence bytes — instead of only a manifest, so there is
something for the sweep to purge. A deleted project is purged immediately by `delete_project`
itself rather than by a lagging sweep. Application log retention is enforced here too, by the
same sweep, since the file sink landed.

**The stack schedules its own retention sweep (Phase 12B).** `docker/compose.yaml` runs a
`retention` service on `retention --every 24h` in the console image. A scheduling mode on the
console rather than a `cron` sidecar, because the images are chiseled and a sidecar would put a
shell back into a stack that has nothing in it to execute. It stays **outside** the pipeline for
the reason the sweep was put there: a pass spans every workspace and project and has no caller to
authorise it against, so the loop acquires no actor, no membership and no tenant context. First
pass is immediate, a failed pass is reported and the loop continues, and a scope is created per
pass rather than held for the process. `--every 24` is refused rather than read as twenty-four
days.

**A token is never written to a log unless an operator asked (Phase 12B).**
`Email:AllowTokensInLog` is false, so with `EmailOptions.Provider` on its default of `Log` the
fallback sender records that a message could not be delivered, to whom, and how to fix that, and
writes the token nowhere. v1 wrote them unconditionally and `release-readiness.md` carried that as
an accepted risk. Consequence to know: **inviting somebody still works with no mail server**
(`create_user_account` returns the setup token in its own response), and **self-service recovery
deliberately does not** until SMTP or the opt-in is set. Since Phase 13 an administrator can issue
the reset instead: `issue_password_reset` (human-only, `ManageAccounts`) returns a single-use
recovery token in its own response, on the Members screen. It is refused unless the caller
administers **every** workspace the person belongs to, because a password works in all of them. Refusing to start without a delivery
channel was the alternative and was rejected — it breaks a plain `docker run`.

**Application logs: option 2 of `docs/operations/logging.md` is the option in force**, confirmed
2026-09-10. **It never delivered a log line until Phase 13:** with the file sink on, Serilog owned the
pipeline and the OpenTelemetry exporter received nothing. `AddDevBuddyFileLogging` now writes to the
other providers too; the observability end-to-end test found it. Logs go to Loki through the observability overlay and its ninety days is the retention
of record; `Telemetry:ExportLogs` still defaults to false in code (log export is a third egress
path) but the overlay sets it true, which it could not do while tokens were in those logs.

**`linux/arm64` container images are built and started.** The Dockerfiles cross-compile —
`--platform=$BUILDPLATFORM` on the SDK stage, `-a $TARGETARCH` for the output — so an arm64 image
costs a release minutes rather than the hours emulating a compiler would. Nothing RUNs in a runtime
stage, which is what makes that possible. Through `v1.1.0` they were started only under emulation.
For `v1.2.0` all three, and the native `linux-arm64` archive, ran on arm64 hardware: Docker
Desktop's Linux VM on the owner's Apple M4. That is still not a server-class host. On 2026-09-14
the Compose stack of `v1.2.0` ran from clean on arm64 too, with the published images, in an
Ubuntu 26.04 VMware guest (2 CPUs, 5.3 GB). `docs/operations/release-matrix.md` records it. The release workflow checks the non-root user **per
architecture**, because a manifest list can hold one image that drops root and one that does not.

**`win-arm64` is a client platform (2026-09-24, `info.md`).** Every release owes a smoke test of
the console and `DevBuddy.McpServer --stdio` there; **the API in the same archive is not supported
on it**. It first passed against `v1.6.0` the same day, so no RID is left unverified. What follows
is the history. **`linux-musl-arm64` is verified since 2026-09-21**
(Phase 13, B10), so every release owes it a smoke test in Alpine on arm64. `win-arm64` first ran
for `v1.2.0` on 2026-09-14 in a VMware VM on Apple silicon. On 2026-09-22 the owner asked to
backfill every skipped release, so every published archive has now passed there and its checksum
and provenance have been checked. It remains unverified because there is still no future
per-release obligation; nobody may describe it as supported. **`osx-arm64` is verified since
2026-09-13**: it ran natively on the owner's Apple M4 Mac mini, including the Linux-built archive,
which the SDK ad-hoc signs, so every release now owes a smoke test of it on that machine. **`osx-x64` is not published at
all since 2026-09-13**: the owner dropped it, the release workflow no longer builds it, and it must
not come back without a decision in `info.md`.

**Phase 12C's ADRs are confirmed and all of it is written.** ADR-0012 (embeddings and vector
search) and ADR-0013 (the `knowledge-ai-worker`) were confirmed in `info.md` on 2026-09-10, so
their constraints are binding: **embedding text is egress**, so SB-17 and SB-18 apply before text
leaves and the verification matrix gains rows of its own rather than being read as covered; and **a
background worker either holds a machine token with a real membership or touches nothing a person's
permissions would gate**, with nothing permitted in between.

**The embedding provider is a port with two modes, off by default** — a self-hosted model or a
hosted API, the same shape `EmailOptions.Provider`, `EvidenceStoreOptions.Provider` and
`GitHubOptions.Mode` already use. Building the port and both adapters is unblocked. **Enabling the
hosted mode in a deployment is not**: it needs the vendor named, its host in
`OutboundAccess:AllowedHosts`, and an acceptance of its own, because it is a path out of the
boundary the 2026-09-10 acceptance does not cover. No default may turn it on. The self-hosted mode
has no egress, and a secret still may not be embedded into a local index — that index is a data
copy SB-27 covers.

**The worker exists in `DevBuddy.Application/Workers/`: two job types and no third,
`WorkerCaller` (private constructor, one factory taking a resolved machine token), `WorkerBudget`
(refuses rather than throttles, all-or-nothing spends, zero is a legitimate off switch), both
jobs, and `EmbeddingGateway`.** The installation shape's reach is enforced by
**allow-list** and mutation-checked against a job that deliberately reaches too far.

**A worker's channel follows whether the job sends content to a model — not whether it is a
worker.** This is the sharp bit, and the first version of it was wrong in a way only writing a job
revealed. A job that feeds a model runs on `AccessChannel.Ai`, because that is where the
per-project AI access policy and the SB-18 redaction are applied, both on the strength of the
channel alone. A job that touches no model runs on `AccessChannel.InternalSystem`, because **the AI
channel is also an allow-list** (twenty operations today) and everything a worker is for —
`detect_staleness`, `sync_sources`, `reindex` — is `AiExposure.Denied`. `InternalSystem` is not a
bypass: `AuthorizationService` has never special-cased it and still requires an enabled account, a
live membership and a role carrying the permission. The declaration lives on the **job type** so it
cannot vary per run, and a job that declared no model use and reaches for one is refused by the
gateway.

**`EmbeddingGateway` is the only door to `IEmbeddingProvider`, and an architecture test fails the
build if anything else touches it.** It is where ADR-0012's rules are applied rather than
described: **SB-17 scans before text leaves**, because a vector cannot be scanned afterwards and
scanning the response would be scanning the wrong copy. It refuses four things — no provider
configured, a caller off the AI channel, a secret in the text (nothing sent), and a provider that
returned fewer vectors than it was given texts, since an index built from a short answer is
misaligned against the records it describes and nothing about it looks wrong. A hosted provider
**refuses to start** unless its host is in `OutboundAccess:AllowedHosts`.

**The derived vector index exists: `PostgresEmbeddingIndex` behind `IEmbeddingIndex`.** Three
things about it are easy to get wrong.

- **Its migration is conditional, and must stay so.** `pgvector` is an extension and
  `postgres:17-alpine` — the shipped image — does not carry it. An unguarded
  `create extension vector` fails inside `migrate`, which every deployment runs and both servers
  wait on, so a stack that never asked for embeddings would stop starting. `DEVBUDDY_DB_IMAGE`
  selects `pgvector/pgvector:pg17` for an installation that wants it; the default must never carry
  pgvector and a test enforces that. **An installation that migrated without pgvector has those
  migrations recorded as applied,** so moving its existing volume to pgvector by chown leaves the
  table uncreated for good. Restore a backup into a fresh volume instead, which runs every
  migration with pgvector present. That is how the LXC devbox was moved on 2026-09-26.
- **The table has no EF entity, so it has no global query filter.** The scope in every `where`
  clause is the only isolation it has. There is no port method that can be called without a
  `ProjectScope`, and the test that proves it seeds two projects with identical vectors so only the
  `where` clause can refuse.
- **The model and the dimension are matched on every query.** Comparing vectors across models does
  not fail; it returns a confident ranking of nonsense. The column has no declared dimension on
  purpose, so no approximate-search index can be built over it and a similarity query is an exact
  scan bounded by the scope.

`delete_project` purges the index explicitly, because no cascade would have taken it. The index
holds **no text** — identifiers, a content hash, a model, a dimension and a vector.

**The AI surface is twenty operations, not the eighteen v1 shipped.** It moved twice on
2026-09-11, the first movement since Phase 7: `search_similar_records`, then `list_records` from
`Denied` to `Allowed` so the embedding sweep could enumerate a project from the AI channel it has
to run on. Both needed `ReadKnowledge`, which `search_knowledge` already needs, so the surface grew
by two tools and by no privilege. An operation of its own rather than a mode of `search_knowledge`,
because the two differ in what they *do*: full-text search is free and local, this one embeds the
query, which on a hosted provider is egress and costs money per call. It needs `ReadKnowledge`, the
permission `search_knowledge` already needs — the surface grew by a tool, not by a privilege. It
returns identifiers, titles, kinds and distances and **never content**; a caller that wants a
record calls `get_record`. The query is scanned twice on purpose: the pipeline's SB-17 pass
protects what is stored and audited, the gateway's protects what leaves. It answers with a
**reason** rather than an empty list when there is no provider, no index, or nothing indexed,
because a caller that cannot tell those from "no match" will read them as "no match".

**`record-embedding-sweep` is the job that writes the index**, and the only thing that does. It
declares `SendsContentToAModel`, so it runs on the AI channel, and that is what makes it safe
rather than a formality: `list_projects` omits a project nobody opened to AI, so **a project nobody
opened to AI is never embedded**; SB-18 redacts what the job reads; and SB-17 scans twice, so a
record carrying a credential is skipped with nothing sent and the sweep continues. It embeds **the
published revision and nothing else** — a draft is not knowledge yet, and indexing one would let a
semantic search surface something nobody approved. It re-embeds only what changed, keyed on the
content hash **and the personal-data rule set's fingerprint** (Phase 13, D5), so an unchanged
record costs neither a read nor an embedding, but a change to the SB-18 rules re-embeds every
record once, within the budget, because the redacted text it embedded is no longer what it would
send. Bump `PersonalDataRules.ChecksVersion` when a rule's acceptance check changes without its
pattern changing; the fingerprint cannot see code. **A long record is embedded in chunks** (Phase
13, D9): `TextChunks.Split` cuts at most `Embedding:ChunkCharacters` (3000) with a tenth's overlap,
all chunks go in one gateway call so the budget and the SB-17 scan stay all-or-nothing per record,
the index keeps a row per chunk (`chunk` column, migration `RecordEmbeddingChunks`, conditional like
the table), and a similarity query ranks a record by its best chunk and answers it once. **An archived record keeps its
published revision**, so since 2026-09-17 the sweep removes its rows, and a newer published
revision replaces the older one's row rather than ranking beside it. `search_similar_records` also
answers only a record's current published revision, and never an archived record, because the
index lags by up to a pass.

**`WorkerBudget` counts texts, not calls.** A correction: the gateway takes a batch, so counting
invocations meant a budget of ten bounded nothing. Providers charge for input. Spent
all-or-nothing, so a batch a run cannot fully afford is not started rather than half-sent.

**The schedule exists (2026-09-13): `worker <job> --every <interval>` on the console, and two
Compose services behind a `workers` profile.** A plain `up -d` starts neither. Both jobs run
**inside** the pipeline as the owner of a machine token from `DEVBUDDY_WORKER_TOKEN`, one token per
job, and the console resolves that token **again on every pass** — so revoking it on the Teams
screen stops the next pass without a restart — enters the token's own workspace and no other, and
gives each pass a fresh budget. `record-embedding-sweep` must be given `--budget` and Compose
defaults it to zero; `stale-record-sweep` must be given `--stale-after` and has no default. A pass
that cannot run is reported as **refused**, not as an error, and the schedule continues. Two things
that are easy to get wrong: **`stale-record-sweep` needs `ManageIndex`**, which since Phase 13 the
narrow `IndexMaintainer` role carries alongside Administrator, so its token's owner holds that role
and nothing wider (and authorization asks whether *any* covering grant carries a permission,
because that role is numbered after Administrator and is not above it); and the MCP server
did not receive the embedding settings in Compose until this change, so semantic search would have
answered "no provider" to every assistant on an installation that enabled one.

**SB-34 found a defect before it proved anything.** `record-embedding-sweep` read `revisionNumber`
from `view_record_history`, whose `RevisionSummary` serialises `number`, so on a real installation
every published record was counted as skipped and the sweep reported success having embedded
nothing. Its unit test passed throughout because the fake answered in the same wrong shape. The
fakes in `RecordEmbeddingSweepJobTests` are now built from the real response records — **do not go
back to anonymous objects there**. `EmbeddingEgressTests` runs the real job and the real search over
a pgvector database with a recorder behind the real HTTP adapter, and asserts on what arrived.

**The self-hosted mode has run end to end, once, on synthetic data (2026-09-13).** On the owner's
test installation, with `BAAI/bge-small-en-v1.5` served by text-embeddings-inference: the first pass
embedded the three published records of the project opened to AI and skipped its draft, nothing
from the closed project reached the model or the index, a second pass sent nothing, semantic search
ranked the matching record first, a revoked token refused the next pass, and the worker's calls are
in the audit trail under its own Viewer account. **Moving an existing database volume onto
`pgvector/pgvector:pg17` restart-loops on `Permission denied`**, because that image runs PostgreSQL
as uid 999 and the Alpine default as uid 70; `docs/operations/deployment.md` has the fix.

**What does not exist:** a hosted provider enabled anywhere, and any provider or worker on any
installation holding real project data — both still need an approval of their own. **The devbox
was approved (`info.md`, 2026-09-16):** self-hosted `qwen3-embedding:0.6b`
through Ollama inside the stack, real project data with no customer, production or personal data,
no bounded scope, and `record-embedding-sweep` as a Viewer account of its own at 50 texts every 24h.
From 2026-09-22 `stale-record-sweep` ran there too, as an account holding only `IndexMaintainer`, every 24h with `--stale-after 365d` (`info.md`). **That worker ran there from 2026-09-17**, on v1.3.0, as the
owner's `test_worker` account with a token it minted. Its first pass with something to embed
indexed one published test record, and `search_similar_records` found it over the plugin.
**None of that survived the move to the LXC on 2026-09-25**, and that approval did not carry over.
**The owner approved the LXC on 2026-09-26** (`info.md`), on the same terms plus the stale-record
sweep and Qwen3's query instruction. The two workers run as accounts of their own, with 365-day
tokens and no password.

**The console logs to standard error (2026-09-17).** With `Logging:File:Path` set it used to write
every SQL statement to standard output ahead of a `run` result, so nothing could parse that output.
`ConsoleLogging` sends the console half of the file sink to standard error, as the MCP server under
stdio always did. Two other lines that read like faults are gone too: every host now states EF's
single-query loading (the default it already used, unstated it warned on every record read), and
switches Npgsql's GSS encryption probe off unless the connection string chooses, because the
chiseled images have no `libgssapi_krb5.so.2` and the probe printed that it could not load it.

**Telemetry is OpenTelemetry, off unless an endpoint is configured.** `Telemetry:Endpoint` is
empty by default and `AddDevBuddyTelemetry` registers nothing when it is. Configured, it exports
OTLP traces and metrics; `docker/compose.observability.yaml` is an optional overlay carrying a
collector, Prometheus, Loki, Tempo, and Grafana with a provisioned security-controls dashboard.
Log export is a *separate* opt-in, false in code; the observability overlay turns it on, which it
could not do while setup and recovery tokens were written into those logs. The tagging rule — operation name, outcome,
channel, scanner rule name, and nothing else, ever — is stated in `DevBuddyTelemetry` and enforced
by tests; it is why database instrumentation is absent and why URL paths are scrubbed from spans.
**`OpenTelemetry.Instrumentation.AspNetCore` must not go in Infrastructure**: its framework
reference propagates to the console, whose image uses the smaller `runtime` base, and the
container then fails to start at all. The two web hosts add it themselves through the
`configureTracing`/`configureMetrics` callbacks.

**Evidence can now be attached, not only read.** `capture_evidence` and `list_evidence` close a
gap nobody had noticed: `download_evidence` existed, backup and restore carried the bytes,
retention swept them and the isolation tests covered them, but no operation, endpoint or screen
ever called `IEvidenceStore.StoreAsync` — only tests did — so a real installation could never have
had anything to download. Capture is human-only and runs through the pipeline, so a file carrying
a credential is Blocked with **nothing written**, the same shape a draft gets (SB-17): the scan
happens before the store is touched, which is why the request carries an array rather than a
stream. `RecordScanResultAsync` moved onto `IEvidenceStore` — the implementation had existed since
Phase 3 with no way to call it — because stored evidence begins `NotScanned` and the download
refuses to release anything in that state. Capture and download have **streaming routes of their
own** rather than dispatcher entries, because base64 in a JSON envelope inflates a file by a third;
`list_evidence` is ordinary JSON and is dispatched normally. The `Evidence` screen under a project
is where a person does it.

**A project can be deleted.** `delete_project` (Administrator, workspace- or project-scoped) removes
its work items, records with their full revision history, evidence rows and bytes, source
repositories, and project-scoped memberships, immediately. Audit history survives the project it
describes — deleting content is not the same as erasing that the deletion happened.

**A team can be created, renamed, staffed, and deleted.** The entity and its table existed since
Phase 1 with nothing reading or writing them; `create_team`, `rename_team`, `delete_team`,
`list_teams`, `list_team_members`, `add_team_member`, and `remove_team_member` close that. A team
still carries no permission of its own — `Membership` decides what anyone may do, exactly as
before.

**A second workspace can be created — by an existing workspace administrator, not by a new
"installation administrator" role.** `create_workspace` takes a sponsor workspace the caller
already administers, authorises against it through the ordinary pipeline, and makes the caller
administrator of the new one, the same shape `IInstallationBootstrapper` uses for the first one.
`IInstallationBootstrapper` is unchanged: it still refuses once any workspace exists, because it is
still the zero-membership case with nobody to authorise. There is no installation-wide superuser
concept anywhere in this system.

**Setup and recovery tokens are delivered by `IEmailSender`.** `EmailOptions.Provider` defaults to
`Log`, which since Phase 12B records that a message could not be delivered and writes the token
nowhere unless `Email:AllowTokensInLog` is set; see above. Setting `Provider` to `Smtp` (MailKit)
delivers it for real, to the account's own address. **Neither sender throws when delivery fails**
(2026-09-26). The SMTP sender logs the recipient, the subject and the server, never the body, and
returns. Until then a broken mail server made account recovery answer 500 for an address with an
account and 202 for one without, which told anybody who asked who had one. It also made
`create_user_account` answer 500 after writing the account. Do not let a send failure reach a
caller. Delivery still happens inside the request, so recovery takes longer for an address that
has an account while SMTP works. That timing difference is known and not fixed. The SMTP sender
speaks STARTTLS or nothing: there is no implicit-TLS mode for port 465, and `UseStartTls=false`
sends the password in clear.

**A release is a `v*` tag, and the workflow signs what it publishes.**
`.github/workflows/release.yml` gates on the full suite, then builds every RID in
`docs/operations/release-matrix.md`, pushes the three images to GHCR, generates one SBOM per host,
and attests all of it with GitHub's keyless OIDC identity — no signing key to hold. It leaves the
release as a draft, because the checklist it cannot run (hand-run smoke tests, the restore drill)
is the half a person has to record. `v1.0.0` is published, so SB-29 is `TESTED`; what was re-run
for that tag and what was carried over is recorded in `docs/operations/release-matrix.md`.

**Application log retention is enforced and tested.** `Logging:File:Path` (set by
`docker/compose.yaml`) turns on a Serilog daily file, and `dotnet run -- retention` deletes files
past `Logging:File:RetentionDays` — the same sweep that handles audit events, evidence, backups,
and exports, and the last of the ten schedule rows to leave "operator responsibility". Off unless
the path is set, because the containers run read-only and a default that wrote files would break
every plain `docker run`. **`Serilog.AspNetCore` must not be used**: same framework-reference trap
as the OpenTelemetry ASP.NET instrumentation, and `Serilog.Extensions.Hosting` is what
Infrastructure takes instead. `docs/operations/logging.md` has the three options and what each
costs, and option 2 is the one in force.

**Restore is a console command, not an operation.** Every operation is authorised against a
membership, and a restore from total loss runs against a database with no memberships in it, so
`restore_system` could never have succeeded and is gone. A test asserts no restore operation
exists. Backup is still an operation, because that one has a caller.

**A backup carries rows and artefacts.** It is logical rather than `pg_dump`, because running an
external program from product code would break the no-execution guard. Sessions are not restored;
passwords and machine tokens are. Since Phase 13 a deleted project stays deleted across a restore:
`ProjectDirectory.DeleteProjectAsync` appends identifiers to `deletions.jsonl` beside the backups,
`restore` deletes again whatever was recorded after the backup was taken, and retention prunes the
ledger to the oldest backup left. The ledger is a file, not a table, because the database is what a
restore replaces.

**Every audit entry records the channel its request arrived on (2026-09-15).** A machine token's
owner is an ordinary user, so an assistant's `create_draft` over MCP and the same person's in the
web UI used to leave identical rows. That mattered on 2026-09-15, when drafts created over the AI
channel before a fix were stored as not AI-generated and could not be re-marked, because nothing
said which channel wrote them. Things that are easy to get wrong:

- **It is a column, `audit_events.channel`, not a detail entry.** Investigations filter on it, and
  a detail is optional metadata an entry can be written without. The domain has its own
  `AuditChannel` enum, because Domain references nothing, with the same numbers as `AccessChannel`.
  The executor maps between them exhaustively, so a fourth channel fails loudly rather than being
  recorded as one of the three.
- **Rows from before the migration are null, and must stay null.** Null means "not recorded". It is
  never `Human`, no filter matches it, and the UI shows it as not recorded. Backfilling a guess
  would be exactly the untrustworthy record this was added to avoid. The `AuditEvent` factories
  take a non-null channel, so only rehydrating an old row can produce one without.
- **The bootstrap writes `InternalSystem`.** It is outside the pipeline because nobody exists yet
  to be a caller.
- `read_audit_history` returns the channel and takes an optional `channel` filter. It was the
  first nullable enum on the wire, and it found a bug in the TypeScript client generator: a `null`
  enum member emitted a dangling `|`. That is fixed in `TypeScriptClient`.

SB-19's row in `docs/security/verification-matrix.md` names the tests. They passed on jmhp on
2026-09-15 and were mutation-checked there, and passed in CI in run 35328802725 on 2026-09-18.

**Identity over MCP stdio is a machine token in `DEVBUDDY_TOKEN`, bound to one user and one
workspace.** `DEVBUDDY_ACTOR` is gone: it let anybody who could start the process start it as
anybody, and a test fails if either plugin package mentions it. A token carries exactly its
owner's permissions **in the one workspace it was minted in**, and is revocable on the next call.
`AuthorizationService` refuses a request naming another workspace before it looks the caller up,
so the check costs no query and cannot be reached past. A signed-in person's HTTP session carries
no such ceiling and is unaffected. Tokens issued before this scoping carry no workspace and are
**refused rather than adopted** — the row says who owns it and nothing about where it was meant to
work — and are listed as needing replacement so their owners can mint a successor. Neither plugin
package holds a literal setting any more: both name variables and take them from the environment
the session was launched in, which matters most for Codex, whose file is account-wide.

## Commands

```powershell
dotnet build DevBuddy.slnx -c Release
dotnet test DevBuddy.slnx -c Release
dotnet format DevBuddy.slnx --verify-no-changes --severity warn
```

The console runs migrations and the one-time bootstrap:

```powershell
dotnet run --project src/hosts/DevBuddy.Cli -- migrate
dotnet run --project src/hosts/DevBuddy.Cli -- operations --ai
```

All three hosts read `appsettings.json` in the working directory and environment variables
prefixed `DEVBUDDY_`, and refuse to start without a connection string rather than inventing one.

The whole system is tested end to end by Playwright in `tests/e2e`: `bash tests/e2e/run.sh` builds
the images, starts a throwaway stack under its own Compose project, and drives the web client, the
HTTP API and the MCP server's HTTP transport, in Chromium, Firefox and WebKit. It has 232 tests.
On 2026-09-26, in CI run 36238723460 on `f321f82`, both Playwright runners passed 211 and skipped
21, the tests that belong to the embeddings, GitHub-source and observability modes, which their
own jobs run. It
last ran outside CI on 2026-09-24, at 205, on the owner's Linux test machine, now gone. CI runs it
on amd64 and arm64 runners, with the embeddings, GitHub-source and observability modes. `tests/e2e/README.md` says what it does not cover.
`DEVBUDDY_E2E_ZAP=1` adds OWASP ZAP's baseline scan and API scan against the same stack (Phase 14,
C4). **`tests/e2e/zap/rules.tsv` decides whether they pass:** a finding passes only if a rule there
accepts it by name, with its reason. A rule the file marks `FAIL`, a rule it does not list, a scan
that does not finish, or an unhandled exception the API logs while scanned fails the run. The last
is checked from the logs, because ZAP's rule 100000 cannot tell a 500 from a 401. CI runs the scans
on the amd64 run and puts both reports in the job summary.

The web client uses **Bun**, not npm — that is what this machine has:

```bash
cd web/admin && bun install && bun run build && bun test
```

`web/admin/src/api/operations.ts` is generated from `GET /operations` and committed. Never edit it
by hand; regenerate it with the command in `AGENTS.md` after changing any operation or its
request or response record, or the drift test fails.

The stack runs from `docker/compose.yaml`:

```powershell
docker compose -f docker/compose.yaml up -d
```

Images are chiseled: no shell, no package manager. That is why the API's container health check is
`dotnet DevBuddy.Api.dll --health-check` rather than curl, and why a `RUN` in a runtime stage is
impossible.

## Architecture in one paragraph

One shared core, three thin hosts. `DevBuddy.Domain` holds entities and invariants and references
nothing. `DevBuddy.Application` holds use cases and ports and depends on Domain only.
`DevBuddy.Infrastructure` implements the ports. `Api`, `McpServer`, and `Cli` translate a transport
into a use-case call. Cross-cutting concerns — validation, identity, authorization, redaction,
audit — live in one Application pipeline so no host can skip them. This is what lets the MCP
surface be a deliberate allow-list over existing use cases rather than a second implementation.

## Things that are easy to get wrong here

- **`CR` is banned.** Change Request and Code Review are separate record types. Write
  `change_request` and `code_review` in full, in code, schema, API, and UI.
- **No SQLite.** PostgreSQL in development and production, and in tests via Testcontainers.
- **MinIO is built from its source, pinned by commit, since 2026-09-25 (`info.md`).** Two registries
  have dropped it: Docker Hub by 2026-09-13, and quay.io without a login on 2026-09-24. Each time,
  every machine without a cached copy failed to build the evidence store, and CI went red while the
  owner's test machine, holding the cached copy, kept passing. `docker/evidence/Dockerfile` compiles
  MinIO `RELEASE.2025-04-22T22-12-26Z` and `mc` `RELEASE.2025-04-16T18-13-26Z` by commit, with Go
  1.24.2 pinned by digest, into a `scratch` image holding the two binaries and nothing to execute.
  It is built natively where it runs, never cross-compiled, and a cold build takes three or four
  minutes. `EvidenceStoreTests` builds the same Dockerfile through Testcontainers. `DeploymentTests`
  fails if a commit or the Go image loses its pin, if the runtime is not `scratch`, or if a registry
  MinIO image comes back. Do not go back to a registry image. Until 2026-09-14 the stack ran MinIO as
  uid 0 while its own comments said otherwise, and SB-31's checks only ever looked at the three
  application images.
  - The image runs as uid 1000 and keeps data at **`/srv/evidence`, not `/data`**. That path dates
    from the upstream image, which declared `VOLUME /data`, and existing volumes are mounted there.
    `/data` still exists, owned by uid 1000, only because Testcontainers' MinIO module starts
    `server /data`.
  - Compose runs the service read-only, with every capability dropped and `no-new-privileges`.
  - `DeploymentTests.every_service_in_the_stack_runs_as_a_non_root_user` checks every service. It
    was mutation-checked against the old file.
  - **An upgraded stack needs a one-time `chown -R 1000:1000` of its evidence volume**, or MinIO
    refuses to start with `drive may be faulty`. `docs/operations/deployment.md` has the command.
  - Do not put the volume back at `/data`, and do not set a Compose `user:` instead. Phase 10 tried
    the latter; a fresh volume is then owned by root.
- **Embeddings and vector search are post-v1, off by default, and gated.** v1 shipped full-text
  search and structured filters alone, and an installation that configures no provider still runs
  exactly that. The derived vector index (ADR-0012) is opt-in and needs the pgvector extension the
  shipped `postgres:17-alpine` image does not carry, so its migration skips itself there and the
  index reports as absent. **Enabling the hosted provider mode in a deployment needs an
  `OutboundAccess:AllowedHosts` entry and an acceptance of its own.** Never turn any of it on by
  default. **Voyage AI was withdrawn and removed on 2026-09-23**; no vendor is named. The model
  server is Ollama or LM Studio, and **the mode follows where that machine is**: in the stack or on
  the LAN it is `SelfHosted`, and on a cloud machine it is `HostedApi`, even when the owner runs
  it. `SelfHosted` refuses a public address, a literal at start-up and a name before every call
  (`SelfHostedEndpoint`), because calling an internet server self-hosted would hide the egress.
- **The MCP server's HTTP transport answers POST and nothing else.** A `GET` on it is 405 with
  `Allow: POST`, and that is correct, not a fault. `ModelContextProtocol.AspNetCore` 2.2.0 defaults
  to stateless mode, following protocol revision 2026-07-28 (SEP-2567), so `MapMcp()` maps no `GET`
  stream and no `DELETE`. Routing answers the 405 before authorization runs. An unauthenticated
  `POST` is refused with 401 and `WWW-Authenticate: Bearer`. Probe the MCP server with a `POST`
  when checking an installation. Stateless mode means the server cannot push messages to a client,
  and nothing here needs it: every MCP call is a tool call the client makes. Confirmed on the devbox
  on 2026-09-18.
- **Text holding a NUL character is refused before it reaches PostgreSQL** (Phase 14, C4). The
  database cannot store U+0000, and until ZAP found it a NUL in any text was a 500. `TextInput`
  is the rule, applied in `OperationDispatcher` for every operation, by an endpoint filter on the
  `/auth` group, and in the evidence form's handler. **Do not move it into a JSON converter for
  `string`:** a custom converter makes the schema exporter describe every string field as
  anything, which strips the types from the MCP tool schemas and the generated client.
- **The API host sends security headers on every answer** (Phase 14, C4), from `SecurityHeaders`.
  They are set when the response starts, because the exception handler clears headers set
  earlier. The content security policy fits the built client exactly: one same-origin script,
  one stylesheet, no inline code. A client change that needs more, such as an inline style
  element or a font from another host, has to change the policy too, and the e2e suite fails on
  any violation the browser reports. There is no HSTS, because that belongs to the reverse proxy.
  COOP and COEP are sent only when the page arrived over HTTPS, directly or as `X-Forwarded-Proto`
  says: over plain HTTP a browser ignores both, and Chromium logs an error for COOP on every page.
- **Warnings are errors.** Fix them rather than suppressing them.
- **AI access is denied by default per project.** Nothing reaches the MCP tool surface unless it is
  added to the allow-list on purpose.
- **Whether an AI wrote a record comes from the channel, never from the request.** Since
  2026-09-15 `Provenance.IsAiGenerated` is stored, not computed from `SourceKind`. `create_draft`
  and `revise_draft` take `DraftProvenance`, which has no such field. `ForCaller` marks anything
  arriving on `AccessChannel.Ai`. A person declaring `AiDraft` is honoured, and a revision of AI
  content stays marked. Until then, a draft written over MCP that named `RepositoryAnalysis` was
  published as a person's work. **Rows written before the fix cannot be corrected from the data**,
  because nothing recorded the channel a draft came from. Since Phase 13 an administrator who
  *knows* can record it: `mark_record_ai_generated` (human-only, `ManageProjects`, reason
  required) marks every unmarked revision with who said so, when and why (`Provenance.AiMarking`).
  It goes one way only, leaves content and content hash untouched so approvals stay bound, and is
  the one change `UpdateRecordAsync` accepts on a stored revision. Do not put the domain
  `Provenance` back on a request record.
- **The project in a scope is a claim, like the workspace.** Since 2026-09-17
  `AuthorizationService` treats a project that is not a live project of the named workspace as
  covered by no grant, with the same denial as any other unreachable scope. Before that, a workspace
  administrator could write rows against another tenant's project identifier or a made-up one.
  A workspace-level request that names a project in its body, like `grant_membership`, has to
  check it itself. Rows from before are reported by the read-only `scope-report` console command,
  not migrated (`info.md`). Since Phase 13 the operator can delete them with
  `scope-report --delete --confirm <count> --actor <id>`: refused unless the count still matches
  the report and the actor administers every workspace involved, and audited per project as
  `StrayScopeRowsPurged` on the internal channel. Nothing runs it on its own. The Playwright suite in `tests/e2e` found this, and
  `bash tests/e2e/run.sh` runs it against a throwaway stack.
- **Never execute repository scripts** — no builds, restores, or tests — while analysing a
  repository under study. Analysis is read-only.
- **`IMPLEMENTED` is not `TESTED`.** In `docs/security/verification-matrix.md`, a row moves to
  `TESTED` only when a passing test exists. Never report a control as done before that.

## Reporting

State what was actually verified. If a build or test was not run, say so. If a phase exit criterion
is unmet, say which one. The whole point of this project is knowledge that a later owner can trust,
and that standard applies to how the work on it is described.
