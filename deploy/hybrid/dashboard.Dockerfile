# =============================================================================
# comuki dashboard image (hybrid contour) — Vite SPA served by nginx, with
# nginx reverse-proxying /api, /realtime, /openapi to the in-cluster
# orchestrator Service. One origin in the browser: no CORS preflight, cookie
# auth just works.
#
# Build context = repo root (kaniko: KANIKO_DOCKERFILE=deploy/hybrid/
# dashboard.Dockerfile, context .). Same shape as the OSS
# deploy/compose/docker/dashboard.Dockerfile, with two contour-specific
# differences:
#
# 1. Build stage is node:22-alpine, NOT oven/bun. Investigated live on
#    runner-01 (2026-09-11): its VM CPU is "QEMU Virtual CPU version 2.5+"
#    (qemu64 — no SSE4.2). Bun requires SSE4.2, so every bun invocation
#    that runs JS dies with SIGILL (exit 132), and bun 1.4.0's installer
#    livelocks at 99% CPU — this is what pipeline #13's "kaniko runner
#    does not reach registry.npmjs.org" actually was. registry.npmjs.org IS
#    reachable from the runner (curl 200 in ~0.1s; bun 1.3.10 installs the
#    full 608-package tree in 7s). Node/V8 needs no new instructions and
#    vite+rolldown native binaries run clean on that CPU (verified:
#    npm install 76s + vite build 4.3s, DIST_OK).
# 2. The build runs `vite build` directly, not `npm run build`: the npm
#    script chains `tsc -b` (typecheck of test files too) and there is no
#    lockfile in the repo, so npm may resolve newer dev-dependency minors
#    than bun did locally (seen: @testing-library/react 16.4 dropping
#    `screen` re-exports broke tsc while vite stayed green). Typecheck
#    remains a dev-side gate; the image build only needs the bundle.
#    `--legacy-peer-deps` mirrors bun's lenient peer resolution (storybook
#    8 does not declare vite 8 peer support yet).
#
# VITE_API_BASE_URL is BAKED at build time (Vite import.meta.env). It must
# be NON-EMPTY in real mode — kubb-client.ts treats "" as "mock mode" and
# throws on every generated hook call. Same-origin is expressed by pointing
# it at THIS deployment's public origin: nginx proxies /api on the same
# host, so the browser still makes same-origin requests (cookies included).
# =============================================================================

# ---------- Stage 1: build the SPA ----------
FROM docker.io/library/node:22-alpine AS build

ARG VITE_API_BASE_URL=http://app.comuki.nova.adcluster.targetix.net
ARG VITE_DEPLOY_ENV=staging
ARG COMMIT_SHA=""

WORKDIR /src

# Dependencies first for layer caching. No lockfile is committed (the
# dashboard team installs with bun locally), so npm resolves from
# package.json — same freshness contract as the OSS dockerfile's bun install.
COPY dashboard/package.json ./
RUN npm install --no-audit --no-fund --ignore-scripts --legacy-peer-deps

# Sources (the build context excludes node_modules/dist via .dockerignore,
# so this cannot clobber the tree installed above).
COPY dashboard/ ./

# Real backend mode, same-origin via the deployment's public origin.
ENV VITE_USE_MOCK=false \
    VITE_API_BASE_URL=${VITE_API_BASE_URL} \
    VITE_DEPLOY_ENV=${VITE_DEPLOY_ENV} \
    COMMIT_SHA=${COMMIT_SHA}

RUN ./node_modules/.bin/vite build \
    && test -f /src/dist/index.html \
    && echo "SPA build OK: $(ls /src/dist/ | wc -l) files"

# ---------- Stage 2: nginx ----------
FROM docker.io/library/nginx:1.27-alpine

COPY deploy/hybrid/dashboard.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 80
