# The evidence store: MinIO, built here from its source, and run as a non-root account.
#
# Built from source since 2026-09-25 (info.md). Docker Hub stopped serving minio/minio by
# 2026-09-13, and quay.io stopped serving it without a login on 2026-09-24. Both times, every clean
# install and every CI run failed, while machines with a cached copy carried on as if nothing had
# happened. MinIO's source is public, so building it needs no registry of MinIO's.
#
# The same releases the pinned image carried, fetched by commit rather than by tag, so no one can
# move what is built. They are built with the Go version both upstream binaries report (1.24.2), and
# stamped with their release names the way upstream's own release builds are. They are the same
# source and the same compiler, but not byte-identical to upstream's binaries. DeploymentTests fails
# if a commit or the Go image stops being pinned. EvidenceStoreTests builds and starts this same
# Dockerfile, so the image tested is the image shipped.
#
# Built for the machine that builds it, not cross-compiled. Compose builds it where it runs, and
# nothing is published. Testcontainers builds it through the classic builder, which does not
# provide the BuildKit platform arguments.
FROM golang:1.24.2-bookworm@sha256:79390b5e5af9ee6e7b1173ee3eac7fadf6751a545297672916b59bfa0ecf6f71 AS build

# Static binaries, as upstream builds them, and the pinned Go only: never a downloaded toolchain.
ENV CGO_ENABLED=0 GOTOOLCHAIN=local

# A directory per repository, entered with WORKDIR rather than `cd` inside a RUN (Trivy DS-0013).
WORKDIR /src/minio

RUN git init -q \
    && git fetch -q --depth 1 https://github.com/minio/minio.git 0d7408fc9969caf07de6a8c3a84f9fbb10a6739e \
    && git checkout -q FETCH_HEAD \
    && go build -tags kqueue -trimpath \
       -ldflags "$(MINIO_RELEASE=RELEASE go run buildscripts/gen-ldflags.go 2025-04-22T22:12:26Z)" \
       -o /out/usr/bin/minio

# mc is what the health check runs: `mc ready local`.
WORKDIR /src/mc

RUN git init -q \
    && git fetch -q --depth 1 https://github.com/minio/mc.git b00526b153a31b36767991a4f5ce2cced435ee8e \
    && git checkout -q FETCH_HEAD \
    && go build -tags kqueue -trimpath \
       -ldflags "$(MC_RELEASE=RELEASE go run buildscripts/gen-ldflags.go 2025-04-16T18:13:26Z)" \
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
