# ADR-0014: SeaweedFS replaces MinIO as the default evidence store

- Status: **Proposed** 2026-09-27. It becomes Accepted when the owner confirms it in `info.md`.
  Nothing in the product changes before then.
- Date: 2026-09-27
- Phase: 14 (C6)
- Supersedes: [ADR-0004](0004-minio-evidence-store.md), for the choice of store only. Every other
  decision in it stands.

## Context

ADR-0004 made MinIO the default store behind `IEvidenceStore`, and the owner confirmed it on
2026-09-01. Three things have changed since.

- **MinIO is archived upstream.** `minio/mc` has been archived since 2025-11 and `minio/minio`
  since 2026-04-24. No fix will come from either. Two registries had already stopped serving the
  image, and since 2026-09-25 the stack builds MinIO from source (`info.md`).
- **The image scan fails it.** Trivy has gated the evidence image since C5 (`info.md`, 2026-09-26).
  On MinIO's last releases built with Go 1.26.8, 39 HIGH or CRITICAL findings remain in `minio` and
  23 in `mc`. All of them are in modules MinIO's `go.mod` pins. They are accepted in
  `.trivyignore.yaml` **until 2026-12-26**, and that date is this decision's deadline.
- **The owner asked for a replacement to be planned, then for a SeaweedFS spike** (`info.md`,
  2026-09-26). The spike ran the same day, and `docs/plan-phase-14.md` (C6) records it.
  `tools/spikes/c6-seaweedfs/` keeps what it ran.

What the store has to do is set by the code, not by the product's name for it.
`ObjectStorageEvidenceBlobStore` calls five S3 operations: `PutObject`, `GetObject`,
`DeleteObject`, `ListBuckets` and `PutBucket`. It uses path-style addressing, the AWS SDK's default
checksums, and **SSE-S3 (`AES256`) on every write** (`Evidence:UseServerSideEncryption`, on by
default). Backup, restore, export and retention reach the bytes through the same
`IEvidenceBlobStore`, so they need nothing further. The project's own rules also apply to the
image: built from source pinned by commit, non-root, read-only, nothing to execute but the store,
native on amd64 and arm64, and started by the same tests that ship it.

## Decision

**SeaweedFS is the default evidence store.** It runs as `weed server -s3`, one process holding
master, volume server, filer and S3 gateway. The S3 adapter is unchanged. The filesystem adapter
stays behind the same port, as ADR-0004 kept it.

1. **The image is built from source, pinned by commit,** with Go pinned by digest, into `scratch`,
   as `docker/evidence/Dockerfile` does for MinIO today. The spike built 4.47
   (`c5073360007d28385a33426a42ac3e4ec504c5a3`) with Go 1.26.8. It runs as uid 1000, keeps data at
   `/srv/evidence`, and Compose runs it read-only, with every capability dropped,
   `no-new-privileges`, a tmpfs on `/tmp` owned by uid 1000, and `-logtostderr=true`. SeaweedFS
   writes its logs and gRPC sockets under `/tmp`, and a root-owned tmpfs stops it.
2. **SeaweedFS must never answer an unsigned request, and the stack must prove it rather than
   assume it.** With no credentials configured, SeaweedFS allows all access. The spike showed
   anonymous `GET` returning a decrypted object with 200. MinIO refuses to start instead.
   Three guards, each mutation-checked:
   - Compose passes `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` from `${…:?}` variables, and
     `DeploymentTests` fails if either becomes optional.
   - The container's health probe (point 4) reports **unhealthy** unless an unsigned `ListBuckets`
     is refused with 403. The API and the console wait on that health.
   - `ObjectStorageEvidenceBlobStore` sends one unsigned request on first use and refuses to store
     or serve anything if it is not refused. This covers an installation that runs SeaweedFS, or
     any other S3 service, outside this Compose file. It applies to every S3 store, not only this
     one.
3. **SSE-S3 uses a key the operator supplies**, in `WEED_S3_SSE_KEK` (hex, 256 bits), from a new
   `docker/.env` variable, required like the credentials. That is the role
   `DEVBUDDY_EVIDENCE_KMS_KEY` plays for MinIO today. SeaweedFS refuses to start if a KEK it stored
   earlier differs from the one given. The spike verified the derived-key form (`WEED_S3_SSE_KEY`):
   no plaintext on the volume, a restart with the same key reads back, and a wrong key fails with 500
   and discloses nothing. **The KEK form is to be verified the same way before this is Accepted.**
4. **The health check is a probe of our own.** `scratch` has no shell and no client, so the image
   carries a small static Go program built from this repository beside `weed`. It asks the S3
   gateway's `/healthz` and then makes the unsigned request in point 2. It stands where
   `mc ready local` stands today.
5. **Existing evidence moves by backup and restore, and nothing converts in place.** A backup
   already carries every evidence object, read through the adapter, and `restore` writes each
   one back through it. The upgrade is: take a backup on the old release, start the new release
   with a fresh evidence volume, and restore. The restore drill in `tools/release/` is the test,
   and `deployment.md` gains the steps. Objects are re-encrypted under the new key as they are
   written. The old volume is left in place for rollback.

## Consequences

- **The findings accepted for MinIO go.** The spike's image had one HIGH finding (`grpc`, a `-dev`
  version) against MinIO's 39. It either clears the gate or is accepted with an expiry. None of the
  39 MinIO entries is renewed.
- **The store has an upstream again.** It is Apache-2.0, not archived, and releases roughly weekly,
  so a pinned commit can be moved on a schedule rather than never. Moving it is a pull request that
  passes the image scan and `EvidenceStoreTests`, like any other dependency.
- **An upgrade is a restore, once.** It is heavier than a normal upgrade, and it is the release's
  main note. Everyone signs in again afterwards, because sessions are not restored.
- **The anonymous-access guard is new code that must stay.** The product now depends on a store
  that is open by default. The adapter check is what stops a future Compose edit, or an operator's
  own deployment, from quietly serving evidence to anyone.
- **The binary is large:** 203 MB against MinIO's two smaller ones. Idle memory was lower, 93 MiB
  against MinIO's 205 MiB on the devbox.
- **Test fixtures change.** `EvidenceStoreTests`, `RestoreDrillTests` and the API and security
  fixtures start MinIO through Testcontainers' MinIO module. They move to a generic container built
  from the same Dockerfile, so the image tested is still the image shipped. `DeploymentTests` swaps
  its MinIO pin checks for SeaweedFS's. `stack-drill-tokens.sh` and `upgrade.sh` change their
  version checks.
- **arm64 is not yet proven.** The spike ran on amd64 only. SeaweedFS builds natively for arm64,
  and the Ubuntu arm64 guest has to run the image and the probe before this is Accepted.

## Alternatives considered

- **Stay on MinIO.** It is archived, and 62 findings are accepted with nothing upstream to clear
  them. It fails the rule this repository adopted on 2026-09-26: no permanent exceptions.
- **The filesystem adapter as the default.** It needs no second service and no third-party code.
  The owner rejected it in ADR-0004 because a later move would touch backup, restore and retention
  at once. It would also lose encryption at rest until the adapter gained its own, which means
  writing and owning cryptography this project has so far delegated. It stays as the fallback
  behind the port.
- **Garage.** It is small, a single binary, and maintained. It supports SSE-C only, not SSE-S3,
  so the adapter would send a key with every request and the key would move into the API. That
  undoes the separation SB-32 relies on.
- **RustFS.** It is built as a drop-in MinIO replacement and is Apache-2.0, but it is young.
  Nothing yet shows a maintenance record long enough to bet evidence on. It can be reconsidered
  at the next forced move.
- **Ceph RGW.** It is complete and proven, and far heavier than a single-host stack warrants.
- **MinIO's commercial edition.** It is a licence and a registry this project has no agreement
  for, and the last registry change already broke every clean install.
