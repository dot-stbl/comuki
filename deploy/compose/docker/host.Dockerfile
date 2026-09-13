# Comuki host image — one image, three entrypoints:
#   /app/host      comuki             (orchestrator API + dashboard SPA)
#   /app/migrator  comuki-migrator    (one-shot schema migration job)
#   /app/brain     comuki-brain       (standalone brain host, optional)
#
# The dashboard SPA is built into /app/host/wwwroot/ and served by the
# same ASP.NET Core process via UseStaticFiles + MapFallbackToFile —
# no separate nginx container, one origin, cookies just work.
#
# Build context = repo root:
#   docker build -f deploy/compose/docker/host.Dockerfile \
#     --build-arg COMUKI_VERSION=0.1.0 \
#     -t comuki:local .
#
# Build args:
#   VITE_API_BASE_URL  SPA API base. Empty default = same-origin: the image
#                      serves the SPA from the host process itself, so the
#                      API lives at whatever origin the browser opened
#                      (works on localhost, LAN IPs and public domains with
#                      zero rebuilds). Set an absolute URL ONLY when the SPA
#                      is served from a different origin than the API.
#   COMUKI_VERSION     Stamped into the binaries via -p:VersionPrefix
#                      (surfaced by `comuki version` and /api/v1/version;
#                      the scale supervisor derives the worker image tag
#                      from it — see RELEASE.md). Default 0.0.0 = unstamped
#                      local build.
#
# .NET 10, non-root `app` user (mcr aspnet default, UID 1654).
# curl is installed for container healthchecks.

# ---------- Stage 0: build the dashboard SPA ----------
FROM docker.io/library/node:22-alpine AS spa

ARG VITE_API_BASE_URL=
ARG VITE_DEPLOY_ENV=production

WORKDIR /src
COPY dashboard/package.json ./
RUN npm install --no-audit --no-fund --ignore-scripts --legacy-peer-deps
COPY dashboard/ ./
ENV VITE_USE_MOCK=false \
    VITE_API_BASE_URL=${VITE_API_BASE_URL} \
    VITE_DEPLOY_ENV=${VITE_DEPLOY_ENV}
RUN ./node_modules/.bin/vite build \
    && test -f /src/dist/index.html \
    && echo "SPA OK: $(ls /src/dist/ | wc -l) files"

# ---------- Stage 1: build host + migrator + brain ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Release stamping: 0.0.0 = unstamped local build (worker-image pinning
# falls back to `latest`); release builds pass the tag's semver.
ARG COMUKI_VERSION=0.0.0

WORKDIR /src

# Restore first for layer caching: repo-wide pins + the host graphs
# (their transitive ProjectReferences pull in every module).
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/ platform/
RUN dotnet restore platform/src/host/Comuki.Host/Comuki.Host.csproj \
    && dotnet restore platform/src/host/Comuki.Migrator/Comuki.Migrator.csproj \
    && dotnet restore platform/src/host/Comuki.Host.Brain/Comuki.Host.Brain.csproj

# Publish each host into its own directory: separate dependency
# closures, one shared image — versions between entrypoints cannot
# diverge. AssemblyName ships apphost binaries (comuki, comuki-migrator,
# comuki-brain — issue #54); entrypoints run the apphost directly.
RUN dotnet publish platform/src/host/Comuki.Host/Comuki.Host.csproj \
        -c Release --no-restore -p:VersionPrefix=${COMUKI_VERSION} -o /app/host \
    && dotnet publish platform/src/host/Comuki.Migrator/Comuki.Migrator.csproj \
        -c Release --no-restore -p:VersionPrefix=${COMUKI_VERSION} -o /app/migrator \
    && dotnet publish platform/src/host/Comuki.Host.Brain/Comuki.Host.Brain.csproj \
        -c Release --no-restore -p:VersionPrefix=${COMUKI_VERSION} -o /app/brain

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Healthcheck-friendly runtime (aspnet base ships neither curl nor wget).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/host /app/host
COPY --from=spa /src/dist /app/host/wwwroot
COPY --from=build /app/migrator /app/migrator
COPY --from=build /app/brain /app/brain

# aspnet:10.0 runs as non-root `app` (APP_UID 1654); Kestrel binds 8080
# (ASPNETCORE_HTTP_PORTS from the base image — the quiet fallback; the
# comuki way is [server] port / COMUKI_SERVER_PORT, see deploy/README.md).
EXPOSE 8080

ENTRYPOINT ["/app/host/comuki"]
