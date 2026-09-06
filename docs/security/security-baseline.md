# Security Baseline

**Status: DRAFT — every control below is NOT IMPLEMENTED.** This is the control catalogue the
build works towards. Nothing here is a claim that anything is protected today.
[verification-matrix.md](verification-matrix.md) is the only place that records what has actually
been implemented and tested; this file records what we intend and how it will be proved.

Threats and trust boundaries are in [threat-model.md](threat-model.md).

**Owner.** Every control below is currently owned by the **project owner**, because the project
has one person on it. When more people join, ownership becomes per-control and this column stops
being uniform. Leaving it uniform now is a statement of fact, not an oversight.

**How to read the Verification column.** It names the test or drill that will move the control
from "written down" to "demonstrated". A control with no verification method is not a control.

---

## A. Untrusted content and prompt injection

Boundary TB1, TB2. Governing principle from `info.md`: prompt instructions alone are not a
security boundary.

| ID | Control | Asset | Verification | Residual risk |
|---|---|---|---|---|
| SB-01 | Source files, documents, imported records, and tool outputs are handled as data. No code path interprets them as instructions or as configuration. | A3 | Injection corpus test: hostile text in files, docs, commit messages, issue and PR bodies produces no tool call outside the allow-list. | A model may still be steered into a *permitted* action with a misleading argument. Bounded by SB-07 to SB-09. |
| SB-02 | Tool permissions are enforced in server code, not in prompt or instruction text. | A8 | Tool-surface test runs with instruction files removed; the surface is unchanged. | None identified. |
| SB-03 | Network destinations reachable from analysis are restricted by an allow-list outside the model. | A3, A4 | Egress test: a document naming an arbitrary external host produces no outbound request. | Allow-listed hosts remain reachable by design. |
| SB-04 | Repository analysis never executes builds, restores, tests, or repository scripts. Any future execution is a separate, explicitly authorised, sandboxed feature. | A3 | Test repository containing hostile build and test hooks is analysed; process-spawn assertion proves nothing ran. | None identified while the no-execution rule holds. |
| SB-05 | Filesystem paths are constrained to the authorised project root; traversal and symlink escape are rejected. | A3 | Path-traversal corpus, including symlinks and Windows and POSIX separators. | None identified. |
| SB-06 | URLs are validated against an allow-list, with internal, loopback, and link-local addresses blocked, including after redirects and DNS resolution. | A3 | SSRF corpus covering redirect chains and DNS rebinding. | DNS rebinding remains hard to fully eliminate; re-resolve and re-check before connect. |

## B. AI exposure surface

Boundary TB2. Governing principle: only search, get, analyse, create draft, and generate handover
are ever exposed to AI.

| ID | Control | Asset | Verification | Residual risk |
|---|---|---|---|---|
| SB-07 | The MCP tool list is an explicit allow-list. Anything not on it is absent from the surface, not merely refused. A test asserts the exported list equals the allow-list exactly, on both the stdio and HTTP transports. | A1, A2, A8 | Tool-surface equality test, run per transport. Fails on any addition. | A tool on the list can still be misused within its own scope; bounded by SB-08, SB-09. |
| SB-08 | External AI access is denied by default per project, and enabled only by that project owner. | A1, A3 | A project with AI access disabled returns nothing through MCP while the same query succeeds through the API for an authorised human. | None identified. |
| SB-09 | AI results are narrowed to the requesting user permissions, not to the AI credentials. | A8 | Same MCP query as two users with different memberships returns correspondingly different results. | None identified. |
| SB-10 | Approval, publishing, correction, archiving, source synchronisation, indexing, redaction, auditing, backup, and access management stay under human or internal-system control. | A1, A7 | Absence test: no MCP tool maps to any of these use cases. | None identified. |

## C. Identity, access, and tenant isolation

Boundary TB3, TB4.

| ID | Control | Asset | Verification | Residual risk |
|---|---|---|---|---|
| SB-11 | Identity, membership, and resource permission are verified server-side on every request. A caller-supplied project identifier is never trusted. | A8 | Cross-user, cross-team, and cross-project access tests against every read and write path. | None identified. |
| SB-12 | Isolation covers search results, citations, attachments, caches, and exports, not only single-record reads. | A1, A2, A8 | Isolation test suite that specifically exercises search, export, and attachment download paths. | Newly added query paths must be added to the suite; enforced by review. |
| SB-13 | Passwords are stored using the platform password hasher; repeated failures trigger lockout; authentication endpoints are rate-limited. | A5 | Lockout and rate-limit tests; hash algorithm asserted in configuration test. | Weak user-chosen passwords remain possible; mitigated by lockout, not eliminated. |
| SB-14 | Access tokens are short-lived; refresh tokens rotate and can be revoked; revocation takes effect for subsequent requests. | A5 | Revoked-token test; rotation replay test. | A stolen access token is valid until it expires. Bounded by short lifetime. |
| SB-15 | Account recovery uses a single-use, time-boxed token delivered out of band, and does not disclose whether an account exists. | A5 | Recovery flow tests: reuse rejected, expiry enforced, response identical for unknown accounts. | Recovery is only as strong as the delivery channel. |
| SB-16 | Roles (viewer, contributor, reviewer, administrator) are enforced server-side. The UI hides what the server would refuse; the server refuses regardless. | A1, A6, A7 | Role matrix test per endpoint, executed without the UI. | None identified. |

## D. Sensitive data and egress

Boundary TB5. Governing principle: never send secrets to AI, and never retain them.

| ID | Control | Asset | Verification | Residual risk |
|---|---|---|---|---|
| SB-17 | Secret detection and redaction run before material is retained and again before any response leaves the boundary. Rules cover credential patterns, connection strings, private keys, plus entropy heuristics. | A4 | Secret corpus test at both the retention and the egress point. | AL-2: detection can miss material. Mitigated by SB-20, not eliminated. |
| SB-18 | Customer data, production data, and logs containing personal information are denied by default. Release requires sanitisation or a separately approved, bounded scope. The prohibition on secrets still applies inside any such scope. | A2, A3 | Policy test: unsanitised material is refused; an approved bounded scope returns only what it covers. | Classification depends on correct labelling of sources. |
| SB-19 | Audit records that access happened, not the sensitive payload accessed. | A6 | Audit content test: sensitive fixture values never appear in audit rows. | None identified. |
| SB-20 | Every record keeps provenance and revision history so a missed detection can be found and corrected after the fact. | A1, A6 | Provenance-required invariant test; correction flow test. | Correction does not recall data already sent (AL-3). |

## E. Availability and resource limits

Boundary TB1, TB3.

| ID | Control | Asset | Verification | Residual risk |
|---|---|---|---|---|
| SB-21 | File size, request rate, and concurrent job caps are enforced. | A8 | Oversized upload, request flood, and concurrency tests return controlled errors rather than degrading the system. | A determined attacker with valid credentials can still consume their quota. |
| SB-22 | Analysis operations have an execution time limit and are cancelled when exceeded. | A8 | Pathological input test completes within the limit with a controlled failure. | None identified. |

## F. Knowledge integrity

| ID | Control | Asset | Verification | Residual risk |
|---|---|---|---|---|
| SB-23 | Approval binds to the exact content revision reviewed. Publication using an approval for an older revision is rejected. | A7 | Domain invariant test plus an end-to-end stale-approval publish attempt. | None identified. |
| SB-24 | Revisions are immutable. A change creates a new revision; it never rewrites an existing one. | A1 | Immutability test at the domain and persistence layers. | None identified. |
| SB-25 | Provenance (source, author, timestamp, evidence links) is mandatory on every record, with retained links and snapshot metadata for Git, issues, pull requests, and commits. | A1 | Provenance-required invariant test; snapshot round-trip test. | Origin systems can change or disappear; snapshots record what was seen, not what is current. |
| SB-26 | Drafts are stored and presented separately from published records at every layer, including search. | A1 | Draft-visibility test across API, MCP, and UI. | None identified. |
| SB-27 | Retention and deletion apply to logs, backups, exports, and caches, not only the primary database. Schedule in `docs/plan.md`, Phase 11. | A1, A2 | Purge tests per copy, plus a deleted-project sweep test. | AL-5: backups age out on their own schedule. |

## G. Supply chain and hosting

| ID | Control | Asset | Verification | Residual risk |
|---|---|---|---|---|
| SB-28 | Dependency versions are pinned centrally in `Directory.Packages.props`, with transitive pinning enabled, and scanned for vulnerabilities in CI. | Build | CI vulnerability scan gates the build; a known-vulnerable package fails it. | A zero-day in a pinned dependency is undetected until disclosure. |
| SB-29 | Releases produce an SBOM; artifact and image origins and versions are verifiable. | Build | SBOM produced and attached per release; image digest recorded. | Signing and attestation were out of v1 scope when this was written. The release workflow does both, with GitHub's keyless OIDC identity, and `v1.0.0` carries them; the scope note is kept so a later reader can see the control grew rather than assume it was always this. |
| SB-30 | The database and object store are not published to the public network. Only the API and, where enabled, the MCP HTTP transport are exposed, behind TLS. | A1, A2 | Deployment test asserts the compose stack exposes only the intended ports. | Operator misconfiguration in a custom deployment. |
| SB-31 | Containers run as a non-root user with no Docker socket mount and no unnecessary capabilities. | Hosting | Container inspection test in CI. | None identified. |
| SB-32 | Secrets are supplied through environment or a secret store, never baked into images or committed. Local secret files are gitignored. | A4 | Secret scan over the repository and over built images in CI. | AL-2 applies to the scanner. |
| SB-33 | Backups are taken on a schedule and a restore drill is executed each release, proving data and evidence survive container replacement. | A1, A2 | Destroy-and-restore drill with record and evidence comparison. | Restore is proved for the tested topology only. |

---

## Change log

| Date | Change |
|---|---|
| 2026-09-01 | Created in Phase 0 from the security sections of `info.md`. All controls NOT IMPLEMENTED. |
