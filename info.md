# Project Decisions

## Confirmed Documentation Request — 2026-09-09

- Create a detailed Thai HTML manual covering the DevBuddy plugin, its shared system,
  installation, workflows, tools, administration, operations, and development.
- Use a cream main colour and a claymorphism theme for the manual.

## Confirmed Administration UI Delivery — 2026-09-10

- The API host serves the administration UI. `docker/Dockerfile.api` builds `web/admin` in a stage
  of its own and copies the result into the image's `wwwroot`; there is no second container and no
  separate copy of the built files to place on a host by hand.
- Do not add a static-serving container to the stack. An `nginx` or `caddy` image would bring a
  shell and a package manager into a stack whose three images are chiseled precisely so that there
  is nothing in them to execute, and serving files is not worth that.
- The client calls the API at the root, not under a prefix. `VITE_DEVBUDDY_API` remains as a
  build-time override for an installation that serves the client from a different origin; nothing
  in this repository sets it.
- A host with no `wwwroot` serves the API alone. Running from source is that case, and it stays a
  supported one rather than an error.
- Do not publish source maps. The files are served to anybody who reaches the sign-in page, and a
  map is the whole client in readable form.

## Confirmed Machine-Token Scoping — 2026-09-10

- Bind a machine token to a DevBuddy user **and** a DevBuddy workspace. A token minted in one
  workspace is refused in every other, even where its owner holds a live membership there.
- Do not bind a token to a Claude or an OpenAI account, and do not add a client kind to it. The
  assistants are processes that hold a credential; they are not identity providers for DevBuddy,
  and the same token works in either while both are working in that one workspace.
- Separate tokens per assistant remain available to anybody who wants to revoke or monitor them
  separately. That is a convenience and never an authorization boundary.
- Take the workspace from the request the authorization pipeline has already approved, never from
  anything a caller could set independently.
- Fail closed on the tokens issued before this. They carry no workspace, no workspace can be
  chosen for them without granting authority nobody granted, and so they are refused on every call
  and shown in the interface as needing replacement. Their rows are kept for audit and listing;
  their values are not recoverable, because only a hash was ever stored.
- Do not narrow a signed-in person's HTTP session by any of this. A browser session reaches every
  workspace its owner is a member of, exactly as before.
- Extend the workspace-layout convention from one root per deployment to one root per deployment
  **and** workspace, since the credential a root holds now has a workspace in it. The root's own
  settings state which workspace they are for, the scripts refuse a mismatched pair, and they
  refuse a DevBuddy setting stored as a persistent Windows environment variable.

## Confirmed Workspace Layout — 2026-09-09

- Support working against more than one self-hosted deployment from one machine, by settings that
  are separated per root folder rather than shared for the whole user account.
- Keep those settings in a `.devbuddy` folder beside the checkouts, never inside a repository, so
  they are not committed, do not travel with a clone, and cannot be edited by a pull request.
- Ship the arrangement as a template in `templates/devbuddy-root/` with the reasoning in
  `docs/operations/workspace-layout.md`. It is an operator convention: no product code reads it,
  and it grants nothing the server would not already allow.
- Model several checkouts belonging to one piece of work as one project holding several source
  repositories, not as several projects.

This file records decisions and requirements confirmed by the project owner.

## Confirmed

- Treat this project as a clean start; do not reuse or rely on the previous DevBuddy project unless explicitly requested.
- Add or revise this file whenever the project owner confirms a decision or requirement.
- Implement each agreed capability separately for Claude and Codex.
- Use an MCP-first architecture: one shared knowledge core with a console app, web API, and MCP server; provide separate Claude and Codex plugin packages without duplicating the core logic.
- Do not begin implementation until the project owner explicitly instructs to start.

## Knowledge Records

The system must retain the following information so a later owner can ask informed questions and continue work safely:

- Work identity: identifier, type (`develop`, `enhance`, `fixbug`, `change_request`, or `code_review`), title, goal, scope, exclusions, related repositories/modules, and stakeholders.
- Treat Change Request and Code Review as separate concepts and record types. Do not use the ambiguous abbreviation `CR` as the shared identifier for both.
- Context and references: links to issues, pull requests, commits, documents, environments, and relevant configuration.
- Delivery state: status, owner, last-updated time, acceptance criteria, verification method, and evidence of results.
- Decisions: the decision, rationale, alternatives considered, approver, and decision date.
- Technical knowledge: relevant architecture, data flows, APIs/contracts, dependencies, and important configuration.
- Change impact: affected files/components, migration or rollback requirements, known limitations, risks, bugs, root causes, and workarounds.
- Handover: remaining work, open questions, cautions, and practical next steps for the next owner.
- Code-review history: review feedback, resolution status, and rationale for feedback that is not applied.
- Provenance for every record: source, author, timestamp, and links to supporting evidence.

## Knowledge Storage

- Use a hybrid storage design with PostgreSQL as the source of truth for structured records and their relationships.
- Use PostgreSQL for both development and production in the first release. Do not support SQLite as an application database in the initial scope.
- Keep human-readable technical and handover content as Markdown with front matter or equivalent structured text linked to its database record.
- Store original evidence such as logs, screenshots, and exports as files or object storage; retain their metadata and references in PostgreSQL.
- Use PostgreSQL full-text search and structured filters in the first release.
- Defer embeddings and vector search until an embedding model/provider is selected and separately approved. A future vector index will be a derived search index, never the source of truth.
- Retain links and snapshot metadata for Git, issues, pull requests, commits, and other source systems so knowledge can be verified against its origin.

## Knowledge Tooling and Permissions

- Provide read-only analysis tools for projects, code, documents, architecture, Git history, work items, and test evidence before creating a knowledge record.
- Provide `analyze_change_impact` to identify modules, APIs, tests, and documents affected by a commit or diff.
- Provide `sync_sources` to import authorised source systems, and provide `validate_provenance`, `detect_duplicates`, and `detect_staleness` to preserve knowledge quality.
- Provide `detect_secrets` and `redact_sensitive_data` before logs, configuration, or other potentially sensitive material is retained.
- Support a controlled record lifecycle: `create_draft`, human `approve_record`, `request_correction`, and archive or publish operations.
- Allow a draft creator to approve their own draft, subject to the same permission checks as any reviewer. Record the approver, exact approved revision, timestamp, and whether the approver was also the draft creator in the audit history.
- Provide `generate_handover`, `find_open_questions`, and `find_missing_evidence` to support work transfer.
- Provide `compare_snapshots`, `view_record_history`, and `audit_access` for traceability.
- Provide administrative operations for export, backup/restore, health checks, and access management.
- Expose only safe, scoped operations to AI through MCP: search, get, analyse, create a draft, and generate a handover.
- Keep approval, publishing, correction, archiving, source synchronisation, indexing, redaction, auditing, backup, and access management under human or internal-system control.

## AI Usage

- In the first phase, use Claude and Codex as the user-facing AI. They interpret user requests, call MCP tools, and produce analysis, summaries, and draft knowledge records from sourced tool results.
- Keep the shared knowledge core and MCP tools independent of any AI model so they remain usable by people, automation, and both AI integrations.
- Do not create separate AI implementations for Claude and Codex. Keep their plugins and instructions separate while sharing the same knowledge system and tools.
- Add a separate `knowledge-ai-worker` only when autonomous background work is required, such as scheduled source analysis, embedding generation, stale-record detection, or recurring reports.
- An AI worker must use a configured LLM or embedding API, and must account for authentication, cost, permissions, source provenance, and human approval before important output is published.

## Identity and Access

- Support multiple workspaces, teams, projects, repositories, and environments in the data model from the first version; every record and query must be scoped to its workspace and project.
- Use the product's own login and account system initially.
- Do not integrate organisation login, enterprise SSO, or an external identity provider in the initial version.
- Plan for roles including viewer, contributor, reviewer, and administrator. Enable the simplest local or single-workspace experience first while preserving the multi-project data boundaries.

## Technology and Architecture

- Use .NET 10 for the shared core, console application, web API, MCP server, and other product-owned backend tools.
- Include a web administration UI in the first release, using React, TypeScript, Tailwind CSS, and shadcn/ui.
- The initial web UI must support login, account recovery, workspace/team/project and membership administration, project AI-access policy, draft review/edit/approval, published-record history, audit visibility appropriate to the user's role, and basic system health. AI questions remain in Claude/Codex; do not add a separate web chat to the initial scope.
- Use GitHub as the Git hosting provider; this does not change the decision to use the product's own login.
- Follow SOLID principles and Clean Architecture. Keep domain and application logic independent of UI, transport, persistence, and AI-provider implementations; share that logic across the console, API, and MCP entry points.

## Publishing and Hosting

- Publish .NET executables as self-contained artifacts, targeting all OS/architecture combinations supported by .NET 10 for the console and server workloads in scope.
- Produce platform-specific artifacts and define an explicit runtime identifier (RID) and verification matrix. Document unsupported combinations and dependency limitations rather than claiming universal compatibility.
- Self-contained deployment includes the .NET runtime but can still require native OS dependencies. It does not imply a single-file executable or Native AOT.
- Make all product-owned services self-hosted and deploy them through Docker on user-controlled hosting, VPS, or cloud infrastructure that supports containers.
- Provide Docker packaging alongside self-contained publish artifacts. Define the container-platform matrix separately from the native executable matrix; local CLI/MCP execution details remain to be specified.
- Keep application data and evidence persistent across container replacement. The database and evidence storage must support self-hosted deployment without a mandatory managed SaaS backend.

## Deployment Clarifications

- Self-hosting the knowledge system does not by itself make the previously selected Claude/Codex AI processing or GitHub integration self-hosted. Apply the AI Data Policy below to AI access; GitHub access remains subject to authorised repository scope. Actual projects, credentials, and data-transfer settings must be configured before use.
- Platform support references: [.NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) and [.NET publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/). Recheck support and dependencies when defining the release matrix.

## AI Data Policy

The project owner accepted this policy and the security limitations discussed below. Acceptance records design requirements; it does not start implementation or authorise an immediate transfer of real project data.

- Deny external AI access by default for each project. A project owner may enable Claude/Codex access to that project's source code, documents, decisions, and tests; return only the context necessary for the requested task and within the requesting user's permissions.
- Never send passwords, access tokens, private keys, or connection strings containing credentials to AI. Detect and filter sensitive material before tool responses reach AI and before it is retained in records or logs.
- Deny customer data, production data, and logs containing personal information by default. Require sanitisation or a separately approved, bounded data-sharing scope; the prohibition on sending secrets still applies.
- Deny access to other projects unless the requesting user has permission. Apply this rule to search results, citations, attachments, caches, and exports as well as individual records.
- Require separate approval to enable a future AI worker or embedding provider. Permission to use Claude/Codex does not automatically authorise another provider or background processing.
- Audit data access without copying secrets or unnecessary sensitive payloads into audit logs.
- Configure equivalent file-access boundaries in the AI host when it can read local files directly. MCP filtering alone does not govern direct filesystem access or other tools available to the AI.

## Security Baseline and Threat Model

- Establish a Security Baseline and Threat Model before using real project data. For each identified risk, document the affected asset and trust boundary, required control, verification method, responsible owner, and residual risk. Treat the following as an initial baseline, not an exhaustive threat inventory.
- Prompt injection: treat source files, documents, imported records, and tool outputs as untrusted data. Enforce tool permissions and allowed network destinations outside the model; prompt instructions alone are not a security boundary.
- Tenant isolation: verify identity, membership, and resource permissions server-side on every request. Do not trust a caller-supplied project identifier without authorisation; include search, caches, attachments, and exports in isolation checks.
- Account and token security: protect against password guessing, store passwords securely, define session/token expiration and revocation, and provide a secure account-recovery process.
- Unsafe repository execution: analyse repository files without automatically running builds, restores, tests, or repository scripts. Any required execution must be separately authorised and isolated in a restricted sandbox.
- Tool and API abuse: validate inputs, constrain filesystem paths and URLs, prevent SQL and command injection, and prevent attacker-controlled requests from reaching unauthorised internal services.
- Knowledge integrity: preserve provenance and revisions, separate drafts from published records, and bind human approval to the exact content revision reviewed. Reject publication using an approval for an older revision.
- Data copies and retention: apply the policy to logs, backups, exports, caches, and embeddings. Define retention and deletion procedures for those copies, not only the primary database.
- Software supply chain: verify dependency and artifact origins and versions, check vulnerabilities, and control build, release, and update processes for plugins, executables, and container images.
- Hosting security: restrict container privileges, avoid exposing the database publicly, avoid unnecessary Docker socket access, and manage TLS, secrets, and security patches.
- Availability and recovery: limit file sizes, execution time, request rates, and concurrent jobs. Maintain backups, test restoration, and define incident detection and response procedures.
- Verification must exercise cross-user/team/project access, revoked permissions, malicious source/tool content, sensitive-data leakage, approval revision mismatches, resource exhaustion, and recovery. Document evidence and gaps; written policies do not demonstrate that controls have been implemented or tested.

Security references: [OWASP MCP Security](https://cheatsheetseries.owasp.org/cheatsheets/MCP_Security_Cheat_Sheet.html), [Prompt Injection Prevention](https://cheatsheetseries.owasp.org/cheatsheets/LLM_Prompt_Injection_Prevention_Cheat_Sheet.html), [Multi-Tenant Security](https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html), and [Docker Security](https://cheatsheetseries.owasp.org/cheatsheets/Docker_Security_Cheat_Sheet.html).

## Accepted Security Limitations

- No policy or set of controls guarantees protection against every possible risk.
- Sensitive-data detection can miss material, and human reviewers can make mistakes; preserve traceability and support correction.
- Revoking access prevents subsequent authorised access but does not retrieve copies already downloaded or sent outside the system.
- MCP controls do not cover direct filesystem access or unrelated AI tools; those channels need their own controls.
- Acceptance of these limitations does not waive the required safeguards or constitute blanket acceptance of future vulnerabilities. Review specific residual risks as the threat model, implementation, and test evidence develop.
- The current state is documented requirements only. Security controls have not yet been implemented or verified, and implementation still requires the project owner's explicit instruction to start.

## Confirmed Decisions — 2026-09-01

The project owner approved the phased implementation plan (`docs/plan.md`) and settled the five
design questions it raised. These are confirmed requirements, not proposals.

- Approve `docs/plan.md` as the implementation sequence of record. It converts this file's
  decisions into phases 0-11 with explicit deliverables and exit criteria. Each phase begins only
  after the previous phase's exit criteria are met.
- Evidence storage: use MinIO (S3-compatible object storage) as the default evidence store, shipped
  in the Docker Compose stack. Retain a filesystem adapter behind the same port for tests and
  single-host installations. PostgreSQL remains the source of truth for metadata and references.
- MCP transport: ship both stdio (for locally launched Claude and Codex plugins) and authenticated
  HTTP (for self-hosted or remote use) in the first release, sharing one tool allow-list and one
  authorization pipeline.
- Release matrix (**amended 2026-09-07**; the tiers below are what v1.0.0 actually shipped, and
  what the original decision named is in the amendment note at the end of this file): publish three
  explicit tiers — verified (`linux-x64`, `linux-arm64`, `win-x64`, `linux-musl-x64`), built but
  unverified (`osx-arm64`, `osx-x64`, `win-arm64`, `linux-musl-arm64`), and not published.
  Verified means smoke-tested by hand and recorded per release in
  `docs/operations/release-matrix.md`, not smoke-tested in CI: CI runs the full suite on
  `ubuntu-latest` and `windows-latest` and nothing else. Container images cover `linux/amd64` as a
  separate matrix; `linux/arm64` is not built and not claimed.
- Retention: adopt the default retention schedule in `docs/plan.md` Phase 11, covering records,
  evidence, audit events, application logs, exports, backups, and caches. Record the lag before
  deleted data ages out of backups as a stated residual risk rather than omitting it.
- Work-item source of truth: the product owns the work item and its derived knowledge records.
  Import GitHub issues, pull requests, and commits read-only as linked references and snapshots.
  Do not write back to GitHub; source synchronisation is one-way in the first release.

## Confirmed Decisions — 2026-09-06

The project owner settled how the first release ships. These are confirmed requirements, not
proposals.

- Publish `v1.0.0` rather than hold it. The draft is published only after its attestations are
  verified from outside the workflow that made them, and the release notes state verbatim which
  rows were not verified.
- Re-cut a tag rather than publish one whose source archive is wrong, even when the binaries are
  sound. The draft built from `6a60a48` carried `docker/compose.yaml` from before the MinIO
  encryption key, so anybody deploying from the tag rather than from `main` would have hit a 500 on
  every evidence upload. It was withdrawn and re-cut at `9a8ebf0`. A release is not only its
  binaries.
- Phase 11 is closed and v1 is complete on that publication, with the matrix at 33 `TESTED` of 33.

## Amendment — 2026-09-07

The release-matrix decision of 2026-09-01 is amended above, to the tiers `v1.0.0` actually shipped.
The original read: verified (`linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`), built but
unverified (`win-arm64`, `osx-x64`, musl variants), and container images covering `linux/amd64`
**and `linux/arm64`**.

Two parts of it were never met, and amending the decision is the project owner's choice between
the two ways of resolving that — the other being to hold the release until they were.

- **`osx-arm64` was in the verified tier and has never been run.** No macOS is available to this
  project. It moves to built-but-unverified, alongside `osx-x64` and `win-arm64`.
- **`linux/arm64` container images are not built.** The Dockerfiles have nothing
  architecture-specific in them, so this is a CI change rather than a code one, but until that
  build runs the row would be a guess. The container matrix is `linux/amd64` only.

One part moved the other way: `linux-musl-x64` was in the built-but-unverified tier and has been
run on `alpine:3` each release, so it joins the verified tier. `linux-musl-arm64` has not, and
stays where it is.

The wording changed too. The original said the verified tier is "smoke-tested in CI"; it never was.
CI runs the full suite on `ubuntu-latest` and `windows-latest`, and the smoke tests are performed by
hand and recorded per release in `docs/operations/release-matrix.md`. That file, the release notes
for `v1.0.0`, `docs/plan.md` and ADR-0008 all now say the same thing, which is the point of
amending this rather than leaving one document disagreeing with four.
