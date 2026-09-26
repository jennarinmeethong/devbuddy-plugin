# C6 spike: SeaweedFS 4.47 from source, pinned by commit, into scratch, as the evidence store's
# Dockerfile does for MinIO. Not shipped.
FROM golang:1.26.8-bookworm@sha256:a688600ca24f8a4d3ca77f95b0dd40704a9fc787c826660eb7ba0b641b8b175d AS build
ENV CGO_ENABLED=0 GOTOOLCHAIN=local
WORKDIR /src/seaweedfs
RUN git init -q \
    && git fetch -q --depth 1 https://github.com/seaweedfs/seaweedfs.git c5073360007d28385a33426a42ac3e4ec504c5a3 \
    && git checkout -q FETCH_HEAD \
    && go build -trimpath -ldflags "-s -w" -o /out/usr/bin/weed ./weed
RUN mkdir -p /out/srv/evidence /out/tmp
FROM scratch
COPY --from=build /out/usr/bin/ /usr/bin/
COPY --from=build --chown=1000:1000 /out/srv/ /srv/
COPY --from=build --chown=1000:1000 /out/tmp/ /tmp/
USER 1000:1000
ENV PATH=/usr/bin HOME=/tmp
ENTRYPOINT ["weed"]
