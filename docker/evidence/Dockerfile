# The evidence store: MinIO, run as a non-root account.
#
# The upstream image sets no user, and its passwd file has no account but root, so the stock
# container runs MinIO as uid 0. No other service in this stack does that. Found on 2026-09-14, on
# arm64 and amd64 alike; docker/compose.yaml had said the opposite since Phase 10.
#
# Setting a user in Compose alone is not enough; that was tried in Phase 10. A new named volume
# takes the ownership of the image's mount point, and here that is root. Changing it here is not
# possible either, because the image declares VOLUME /data and a build discards any change to a
# declared volume path. So the data lives at /srv/evidence instead. This image creates that path
# owned by uid 1000, so a new volume mounted there is writable without root. uid 1000 is the one
# upstream's own, deprecated user switch picks.
#
# From quay.io, MinIO's own registry, pinned to its digest: Docker Hub stopped serving minio/minio
# by 2026-09-13. The digest is the one Docker Hub served for this tag, so this is the same build,
# and no registry can move what the tag points at. DeploymentTests fails if this goes back to
# Docker Hub or loses the digest, and EvidenceStoreTests starts the identical reference.
#
# Unlike the three application images, this one is not chiseled: MinIO ships a shell. The RUN below
# executes in that image on the host that builds it. Nothing is cross-compiled, and nothing is
# published; Compose builds it where it runs.
FROM quay.io/minio/minio:RELEASE.2025-04-22T22-12-26Z@sha256:a1ea29fa28355559ef137d71fc570e508a214ec84ff8083e39bc5428980b015e

RUN mkdir -p /srv/evidence && chown 1000:1000 /srv/evidence

USER 1000:1000

# MinIO keeps its certificates directory, and mc its configuration, under the home directory. uid
# 1000 has no passwd entry, so without this they would try the root of a read-only filesystem.
# Compose mounts /tmp as a tmpfs owned by this account.
ENV HOME=/tmp MC_CONFIG_DIR=/tmp/.mc
