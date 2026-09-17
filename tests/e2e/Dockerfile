# The end-to-end runner: Playwright's own image, with this suite and its dependencies in it.
#
# Built from the repository root (see compose.e2e.yaml) because the suite type-checks against the
# client generated for web/admin, and that file lives outside this directory. Dockerfile.dockerignore
# beside this file keeps the context to what the suite needs.
#
# The image tag must match the @playwright/test version in package.json: the browsers in the image
# are the ones that version drives. Both images are pinned by digest, so a rebuild is the same runner.

FROM mcr.microsoft.com/playwright:v1.63.0-noble@sha256:eff16c30e6f3f4af0a03fa4b706120d5e9b0891c344a27d64559aff5900a4a27

# Bun installs from the lock file the repository commits, the same tool web/admin uses. Node, which
# the image already has, runs the tests: Playwright's runner is supported on Node.
COPY --from=oven/bun:1.3.14@sha256:e10577f0db68676a7024391c6e5cb4b879ebd17188ab750cf10024a6d700e5c4 /usr/local/bin/bun /usr/local/bin/bun

WORKDIR /repo/tests/e2e

COPY tests/e2e/package.json tests/e2e/bun.lock ./
RUN bun install --frozen-lockfile

COPY web/admin/src/api/operations.ts /repo/web/admin/src/api/operations.ts
COPY tests/e2e/ ./

# Type errors fail the image, the way they fail the API image for the client itself.
RUN node node_modules/typescript/bin/tsc --noEmit

# Playwright's image ships a non-root account for exactly this.
USER pwuser

ENTRYPOINT ["node", "node_modules/@playwright/test/cli.js", "test"]
