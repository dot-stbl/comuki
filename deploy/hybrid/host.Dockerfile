# =============================================================================
# comuki host image — one image, two entrypoints (console.x pattern):
#   /app/host      comuki             (this Deployment via chart command)
#   /app/migrator  comuki-migrator    (batch Job, deploy/hybrid/migrate-job-dev.yaml)
#
# The dashboard SPA is baked into the host image (console.x single-image
# pattern): Stage 0 builds it with Vite, Stage 2 copies /src/dist into
# /app/host/wwwroot — Kestrel serves it (UseDefaultFiles/UseStaticFiles +
# MapFallbackToFile in HostComposer). No separate nginx dashboard
# deployment anymore.
#
# Build context = repo root (kaniko: KANIKO_DOCKERFILE=deploy/hybrid/
# host.Dockerfile, context .). nuget.config restores from nuget.org only —
# no private feed tokens needed inside the build.
#
# .NET 10, non-root `app` (APP_UID 1654) — matches securityContext in
# deploy/hybrid/dev.yaml (chart default).
# =============================================================================

# ---------- Stage 0: build the SPA ----------
# node:22-alpine, NOT oven/bun: the kaniko runner's CPU is qemu64 without
# SSE4.2 — every bun invocation that runs JS dies with SIGILL (exit 132)
# and bun 1.4.0's installer livelocks (pipeline #13 root cause; see
# deploy/hybrid/vendor note in README.md). Node/V8 and the vite+rolldown
# native binaries run clean on that CPU.
#
# The build runs `vite build` directly, not `npm run build`: the npm script
# chains `tsc -b` (typecheck of test files too) and there is no lockfile in
# the repo, so npm may resolve newer dev-dependency minors than bun did
# locally. Typecheck remains a dev-side gate; the image build only needs
# the bundle. `--legacy-peer-deps` mirrors bun's lenient peer resolution.
#
# VITE_API_BASE_URL is BAKED at build time (Vite import.meta.env) and must
# be NON-EMPTY — kubb-client.ts treats "" as mock mode and throws on every
# generated hook call. It points at THIS deployment's public origin: the
# SPA is served by the host itself, so every API call is same-origin.
FROM docker.io/library/node:22-alpine AS spa

ARG VITE_API_BASE_URL=
ARG VITE_DEPLOY_ENV=production
# Empty base = same-origin: works on both http and https without mixed content.

WORKDIR /src

# Dependencies first for layer caching. No lockfile is committed (the
# dashboard team installs with bun locally), so npm resolves from
# package.json. .dockerignore excludes **/node_modules and **/dist, so the
# source COPY below cannot clobber the tree installed above.
COPY dashboard/package.json ./
RUN npm install --no-audit --no-fund --ignore-scripts --legacy-peer-deps

COPY dashboard/ ./

# Real backend mode, same-origin via the deployment's public origin.
ENV VITE_USE_MOCK=false \
    VITE_API_BASE_URL=${VITE_API_BASE_URL} \
    VITE_DEPLOY_ENV=${VITE_DEPLOY_ENV}

RUN ./node_modules/.bin/vite build \
    && test -f /src/dist/index.html \
    && echo "SPA OK: $(ls /src/dist/ | wc -l) files"

# ---------- Stage 1: build host + migrator ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# CI passes the commit short sha: stamped into the binaries so the compute
# engine's worker-image pinning resolves worker:<same sha> (host and worker
# versions can never diverge). Defaults to 0.0.0 → pin falls back to latest.
ARG COMUKI_VERSION=0.0.0

WORKDIR /src

# Restore first for layer caching: repo-wide pins + the two host graphs
# (their transitive ProjectReferences pull in every module).
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/ platform/
COPY control-plane/ control-plane/
RUN dotnet restore platform/src/host/Comuki.Host/Comuki.Host.csproj \
    && dotnet restore platform/src/host/Comuki.Migrator/Comuki.Migrator.csproj \
    && dotnet restore platform/src/host/Comuki.Host.Brain/Comuki.Host.Brain.csproj

# Publish each host into its own directory: separate dependency closures,
# one shared image — versions between entrypoints cannot diverge.
RUN dotnet publish platform/src/host/Comuki.Host/Comuki.Host.csproj \
    -c Release --no-restore -p:VersionPrefix=${COMUKI_VERSION} -o /app/host \
    && dotnet publish platform/src/host/Comuki.Migrator/Comuki.Migrator.csproj \
    -c Release --no-restore -p:VersionPrefix=${COMUKI_VERSION} -o /app/migrator \
    && dotnet publish platform/src/host/Comuki.Host.Brain/Comuki.Host.Brain.csproj \
    -c Release --no-restore -p:VersionPrefix=${COMUKI_VERSION} -o /app/brain

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=build /app/host /app/host
COPY --from=build /app/migrator /app/migrator
# Brain entrypoint (gRPC :17004) — deployed as a sibling Deployment.
COPY --from=build /app/brain /app/brain
# SPA into the host content root (chart workingDir /app/host): Kestrel's
# UseDefaultFiles/UseStaticFiles + MapFallbackToFile serve it.
COPY --from=spa /src/dist /app/host/wwwroot
# Control-plane profiles (brain reads these at the stable deployment path).
COPY --from=build /src/control-plane/profiles/ /app/control-plane/profiles/
RUN test -f /app/control-plane/profiles/docs-writer.md \
    && test -f /app/control-plane/profiles/explore-readonly.md \
    && test -f /app/control-plane/profiles/implement.md \
    && test -f /app/control-plane/profiles/pr-review.md

# aspnet:10.0 already runs as non-root `app` (APP_UID 1654) and Kestrel
# binds 8080 by default. chart values set workingDir /app/host + command
# [/app/host/comuki].
EXPOSE 8080

ENTRYPOINT ["/app/host/comuki"]
WORKDIR /app/host
