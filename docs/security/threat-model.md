# Threat Model

**Status: DRAFT — no controls are implemented or verified.** This document exists so that the
security work has a target before any behaviour is written. It is an *initial baseline*, not an
exhaustive threat inventory, as `info.md` requires. It is revised at the end of every phase.

Companion documents: [security-baseline.md](security-baseline.md) (the control catalogue) and
[verification-matrix.md](verification-matrix.md) (what has actually been tested).

---

## 1. What we are protecting

| Asset | Why it matters | Where it lives |
|---|---|---|
| A1 — Knowledge records and revisions | The product. Corruption or a silent edit destroys the reason the system exists. | PostgreSQL |
| A2 — Evidence objects (logs, screenshots, exports) | Proof behind a record; often the most sensitive material in the system. | MinIO / object storage |
| A3 — Source code and documents under analysis | Customer or employer intellectual property. | Read from authorised repositories; not copied wholesale |
| A4 — Secrets (passwords, tokens, private keys, connection strings) | Compromise extends far beyond this system. | Must never enter the system at all |
| A5 — Identity and credentials of DevBuddy users | Account takeover grants everything the account can reach. | PostgreSQL (hashed) + token store |
| A6 — Audit history | The record of who did what; the last line of defence when something goes wrong. | PostgreSQL, append-only |
| A7 — Approval bindings | The integrity of "a human reviewed exactly this". | PostgreSQL |
| A8 — Tenant boundaries themselves | Multi-workspace and multi-project isolation is a product promise, not an implementation detail. | Enforced in Application + Infrastructure |

---

## 2. Trust boundaries

```
                    UNTRUSTED CONTENT
      repo files - docs - PR/issue text - imported records - tool output
                            |
                            v
  +--------- TB1: analysis ingestion boundary -------------------------+
  |  All of the above is DATA, never instructions.                     |
  |  No build, restore, test, or repository script is ever executed.   |
  +--------------------------------------------------------------------+
                            |
   AI HOST (Claude/Codex)   |                    HUMAN (browser)
        |                   |                         |
        v                   v                         v
  +-- TB2: MCP boundary --+                   +-- TB3: API boundary --+
  |  allow-listed tools   |                   |  full authenticated   |
  |  AI access denied by  |                   |  surface, role-gated  |
  |  default per project  |                   |                       |
  +-----------------------+                   +-----------------------+
                    \                         /
                     v                       v
              +---- TB4: authorization pipeline ----+
              |  identity -> membership -> resource |
              |  re-checked server-side, every call |
              +-------------------------------------+
                              |
              +---- TB5: egress scan/redact --------+
              |  secrets and sensitive data removed |
              |  before retention AND before egress |
              +-------------------------------------+
                              |
        +---------------------+---------------------+
        v                     v                     v
   PostgreSQL            MinIO/objects          Audit store
                              |
  +--- TB6: data-copy boundary: logs, backups, exports, caches --------+
```

**TB7 — host boundary.** The AI host filesystem and its non-DevBuddy tools sit outside every
boundary above. MCP filtering does not govern them. They require separate configuration in the
host itself, and the plugin documentation says so rather than leaving it assumed.

---

## 3. Adversaries considered

| Actor | Capability assumed | Primary boundary |
|---|---|---|
| Curious insider | A valid account in one workspace; tries to reach another project. | TB3, TB4 |
| Malicious repository content | Controls file, document, commit, issue, and PR text that analysers read. | TB1, TB2 |
| Compromised AI host | The model or its host is induced to request data outside its scope. | TB2, TB4, TB5 |
| Network attacker | Reaches exposed ports on the self-hosted deployment. | TB3, hosting |
| Credential stuffer | Password lists; targets the product login. | TB3 |
| Supply-chain attacker | Publishes a malicious dependency or tampers with a release artifact. | Build and release |
| Departed user | Access revoked, but holds copies taken while authorised. | Accepted limitation AL-3 |

**Explicitly out of scope for v1:** a fully compromised host operating system, a malicious project
owner acting within their own permissions, and physical access to the deployment.

---

## 4. Threats, by boundary

Each threat maps to one or more controls in [security-baseline.md](security-baseline.md).

### TB1 — Analysis ingestion

| # | Threat | Controls |
|---|---|---|
| T1.1 | Repository content carries instructions the AI follows (for example, text telling it to export another project). | SB-01, SB-02, SB-03 |
| T1.2 | Analysing a repository triggers a build script, restore hook, or test. | SB-04 |
| T1.3 | A crafted path or symlink escapes the authorised project root. | SB-05 |
| T1.4 | A crafted URL in a document causes a request to an internal service (SSRF). | SB-06 |
| T1.5 | A hostile file exhausts memory or CPU during analysis. | SB-21, SB-22 |

### TB2 — MCP surface

| # | Threat | Controls |
|---|---|---|
| T2.1 | A newly added use case leaks into the AI tool list by accident. | SB-07 |
| T2.2 | AI reads a project whose owner never enabled AI access. | SB-08 |
| T2.3 | AI reads more than the requesting user is permitted to see. | SB-09 |
| T2.4 | AI performs a human-gated action: approve, publish, sync, redact, backup. | SB-07, SB-10 |
| T2.5 | One transport exposes a wider surface than the other. | SB-07 |

### TB3 and TB4 — Identity, access, tenancy

| # | Threat | Controls |
|---|---|---|
| T3.1 | A caller supplies another workspace or project identifier and is served. | SB-11 |
| T3.2 | Isolation holds for single-record reads but leaks through search, exports, attachments, or caches. | SB-12 |
| T3.3 | Password guessing or credential stuffing succeeds. | SB-13 |
| T3.4 | A stolen token stays valid indefinitely, or revocation does not take effect. | SB-14 |
| T3.5 | Account recovery is abused to take over an account. | SB-15 |
| T3.6 | A viewer reaches approval, audit, or administration functions. | SB-16 |

### TB5 — Egress and sensitive data

| # | Threat | Controls |
|---|---|---|
| T5.1 | A secret in a config file or log is stored in a record, or returned to AI. | SB-17 |
| T5.2 | Customer, production, or personal data is returned without an approved bounded scope. | SB-18 |
| T5.3 | Audit entries accumulate the sensitive payloads they describe. | SB-19 |
| T5.4 | Sensitive-data detection misses material. | AL-2; mitigated by SB-20 traceability and correction |

### Knowledge integrity

| # | Threat | Controls |
|---|---|---|
| T6.1 | A record is published using an approval for an earlier revision. | SB-23 |
| T6.2 | A published revision is edited in place, erasing what was reviewed. | SB-24 |
| T6.3 | A record loses its provenance and can no longer be verified against its origin. | SB-25 |
| T6.4 | A draft is indistinguishable from published knowledge. | SB-26 |

### TB6 — Data copies, hosting, supply chain, availability

| # | Threat | Controls |
|---|---|---|
| T7.1 | Deleted data survives in logs, backups, exports, or caches. | SB-27 |
| T7.2 | A malicious or vulnerable dependency reaches a release. | SB-28 |
| T7.3 | A release artifact or container image is tampered with. | SB-29 |
| T7.4 | The database or object store is reachable from the public network. | SB-30 |
| T7.5 | A container escape via excess privilege or a mounted Docker socket. | SB-31 |
| T7.6 | Secrets are baked into images or committed to the repository. | SB-32 |
| T7.7 | Resource exhaustion makes the system unavailable. | SB-21, SB-22 |
| T7.8 | A backup exists but has never been restored, and fails when needed. | SB-33 |

---

## 5. Residual risks accepted at this stage

Carried from the Accepted Security Limitations section of `info.md`:

- **AL-1** No set of controls covers every risk.
- **AL-2** Sensitive-data detection can miss material, and human reviewers make mistakes. The
  mitigation is traceability and correction, not a claim of perfect detection.
- **AL-3** Revoking access stops future access; it does not recall copies already taken.
- **AL-4** MCP controls do not govern the AI host direct filesystem access or its unrelated tools.
- **AL-5** Deleted data ages out of backups on the backup schedule, not immediately. See the
  retention table in `docs/plan.md`, Phase 11.

Accepting these limitations does not waive any required control, and is not blanket acceptance of
future vulnerabilities. Each specific residual risk is re-reviewed as implementation and test
evidence develop.

---

## 6. Review triggers

Revise this document when a phase completes, a new external integration is added, an AI worker or
embedding provider is proposed, the deployment topology changes, or an incident occurs.
