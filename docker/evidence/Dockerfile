# The evidence store: SeaweedFS, built here from its source, and run as a non-root account.
#
# SeaweedFS since ADR-0014 (info.md, 2026-09-27). MinIO before it was archived upstream, and
# Trivy's image scan failed its last releases on findings nothing upstream would ever clear.
# `docs/adr/0014-seaweedfs-evidence-store.md` has the spike and its verification on amd64 and arm64.
#
# Fetched by commit rather than by tag, so no one can move what is built: SeaweedFS 4.47, built with
# Go pinned by digest, into `scratch`. DeploymentTests fails if the commit or the Go image stops
# being pinned, or if the runtime stage stops being `scratch`. EvidenceStoreTests builds and starts
# this same Dockerfile, so the image tested is the image shipped.
#
# Built for the machine that builds it, not cross-compiled. Compose builds it where it runs, and
# nothing is published. Testcontainers builds it through the classic builder, which does not
# provide the BuildKit platform arguments. A build needs about 5 GB of scratch space.
FROM golang:1.26.8-bookworm@sha256:a688600ca24f8a4d3ca77f95b0dd40704a9fc787c826660eb7ba0b641b8b175d AS build

# Static binaries, and the pinned Go only: never a downloaded toolchain.
ENV CGO_ENABLED=0 GOTOOLCHAIN=local

WORKDIR /src/seaweedfs

RUN git init -q \
    && git fetch -q --depth 1 https://github.com/seaweedfs/seaweedfs.git c5073360007d28385a33426a42ac3e4ec504c5a3 \
    && git checkout -q FETCH_HEAD \
    && go build -trimpath -ldflags "-s -w" -o /out/usr/bin/weed ./weed

# The health probe (ADR-0014, point 4): `scratch` has nothing else a health check could run. It is
# healthy only if the S3 gateway is up AND an unsigned request is refused, because SeaweedFS
# allows all access when it has no credentials.
COPY healthprobe/ /src/healthprobe/
WORKDIR /src/healthprobe
RUN go build -trimpath -ldflags "-s -w" -o /out/usr/bin/healthprobe .

# The data directory, owned by uid 1000, so a new volume mounted there is writable without root.
# /tmp holds SeaweedFS's gRPC sockets; Compose mounts a tmpfs over it owned by the same account.
RUN mkdir -p /out/srv/evidence /out/tmp

# Nothing in the runtime image but the two binaries: no shell and no package manager, like the
# chiseled application images.
FROM scratch

COPY --from=build /out/usr/bin/ /usr/bin/
COPY --from=build --chown=1000:1000 /out/srv/ /srv/
COPY --from=build --chown=1000:1000 /out/tmp/ /tmp/

USER 1000:1000

# Each bucket is a collection of its own to SeaweedFS, and DevBuddy has a bucket per workspace plus
# the canary's. The master pre-grows seven volumes for every new collection while `weed server`
# allows eight, so the second bucket's first write failed with "failed to find writable volumes"
# (found by EvidenceStoreTests, 2026-09-27). One volume at a time, as SeaweedFS's own `weed mini`
# sets it for the same reason; the volume count and size are set in CMD below.
ENV PATH=/usr/bin HOME=/tmp WEED_MASTER_VOLUME_GROWTH_COPY_1=1

# The S3 gateway. Master, volume server and filer run in the same process and are reached by no one
# else: they advertise 127.0.0.1, and only 8333 is meant to be addressed.
EXPOSE 8333

ENTRYPOINT ["weed"]

# One process, logs to standard error, data under /srv/evidence. Credentials come from
# AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY, and the SSE-S3 key from WEED_S3_SSE_KEK; see
# docker/compose.yaml, which requires all three. Volumes of 1 GB rather than 30, as many as the
# disk has room for (`-volume.max=0`), so a small disk still holds many workspaces' buckets. An
# evidence object is at most 100 MB (Evidence:MaxObjectBytes).
CMD ["-logtostderr=true", "server", "-dir=/srv/evidence", "-s3", "-ip=127.0.0.1", "-ip.bind=0.0.0.0", "-volume.max=0", "-master.volumeSizeLimitMB=1024"]
