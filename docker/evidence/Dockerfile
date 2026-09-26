# The evidence store: MinIO, built here from its source, and run as a non-root account.
#
# Built from source since 2026-09-25 (info.md). Docker Hub stopped serving minio/minio by
# 2026-09-13, and quay.io stopped serving it without a login on 2026-09-24. Both times, every clean
# install and every CI run failed, while machines with a cached copy carried on as if nothing had
# happened. MinIO's source is public, so building it needs no registry of MinIO's.
#
# The last releases either project published, fetched by commit rather than by tag, so no one can
# move what is built: MinIO RELEASE.2025-10-15T17-29-55Z, which fixes CVE-2025-62506, and mc
# RELEASE.2025-08-13T08-35-41Z. Both repositories are archived upstream (mc since 2025-11, MinIO
# since 2026-04), so nothing later will come; replacing MinIO is Phase 14 C6. Until 2026-09-26 this
# built the April 2025 releases with Go 1.24.2, which Trivy failed on 44 HIGH and 2 CRITICAL
# findings in minio alone (info.md). They are built with a supported Go, 1.26.8, not the one
# upstream used, because most of those findings were in the standard library; both modules declare
# an older minimum, which a newer compiler honours. They are stamped with their release names the
# way upstream's own release builds are, and are not byte-identical to upstream's binaries.
# DeploymentTests fails if a commit or the Go image stops being pinned. EvidenceStoreTests builds and
# starts this same Dockerfile, so the image tested is the image shipped.
#
# Built for the machine that builds it, not cross-compiled. Compose builds it where it runs, and
# nothing is published. Testcontainers builds it through the classic builder, which does not
# provide the BuildKit platform arguments.
FROM golang:1.26.8-bookworm@sha256:a688600ca24f8a4d3ca77f95b0dd40704a9fc787c826660eb7ba0b641b8b175d AS build

# Static binaries, as upstream builds them, and the pinned Go only: never a downloaded toolchain.
ENV CGO_ENABLED=0 GOTOOLCHAIN=local

# A directory per repository, entered with WORKDIR rather than `cd` inside a RUN (Trivy DS-0013).
WORKDIR /src/minio

RUN git init -q \
    && git fetch -q --depth 1 https://github.com/minio/minio.git 9e49d5e7a648f00e26f2246f4dc28e6b07f8c84a \
    && git checkout -q FETCH_HEAD \
    && go build -tags kqueue -trimpath \
       -ldflags "$(MINIO_RELEASE=RELEASE go run buildscripts/gen-ldflags.go 2025-10-15T17:29:55Z)" \
       -o /out/usr/bin/minio

# mc is what the health check runs: `mc ready local`.
WORKDIR /src/mc

RUN git init -q \
    && git fetch -q --depth 1 https://github.com/minio/mc.git 7394ce0dd2a80935aded936b09fa12cbb3cb8096 \
    && git checkout -q FETCH_HEAD \
    && go build -tags kqueue -trimpath \
       -ldflags "$(MC_RELEASE=RELEASE go run buildscripts/gen-ldflags.go 2025-08-13T08:35:41Z)" \
       -o /out/usr/bin/mc

# The data directory, owned by uid 1000, so a new volume mounted there is writable without root.
# The data lives at /srv/evidence, as it has since 2026-09-14, so existing volumes keep working.
# /data exists for Testcontainers' MinIO module, which starts `server /data`. /tmp is the home
# directory, and Compose mounts a tmpfs over it.
RUN mkdir -p /out/srv/evidence /out/data /out/tmp

# Nothing in the runtime image but the two binaries: no shell and no package manager, like the
# chiseled application images. uid 1000 is the one upstream's own, deprecated user switch picks.
FROM scratch

COPY --from=build /out/usr/bin/ /usr/bin/
COPY --from=build --chown=1000:1000 /out/srv/ /srv/
COPY --from=build --chown=1000:1000 /out/data/ /data/
COPY --from=build --chown=1000:1000 /out/tmp/ /tmp/

USER 1000:1000

# MinIO keeps its certificates directory, and mc its configuration, under the home directory.
# uid 1000 has no passwd entry, so the home directory is named here.
ENV PATH=/usr/bin HOME=/tmp MC_CONFIG_DIR=/tmp/.mc

EXPOSE 9000

ENTRYPOINT ["minio"]
