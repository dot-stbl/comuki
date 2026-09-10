# Comuki host image — one image, three entrypoints:
#   /app/host      comuki             (orchestrator API)
#   /app/migrator  comuki-migrator    (one-shot schema migration job)
#   /app/brain     comuki-brain       (standalone brain host, optional)
#
# Build context = repo root:
#   docker build -f deploy/compose/docker/host.Dockerfile -t comuki:local .
#
# .NET 10, non-root `app` user (mcr aspnet default, UID 1654).
# curl is installed for container healthchecks.

# ---------- Stage 1: build host + migrator + brain ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

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
        -c Release --no-restore -o /app/host \
    && dotnet publish platform/src/host/Comuki.Migrator/Comuki.Migrator.csproj \
        -c Release --no-restore -o /app/migrator \
    && dotnet publish platform/src/host/Comuki.Host.Brain/Comuki.Host.Brain.csproj \
        -c Release --no-restore -o /app/brain

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Healthcheck-friendly runtime (aspnet base ships neither curl nor wget).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/host /app/host
COPY --from=build /app/migrator /app/migrator
COPY --from=build /app/brain /app/brain

# aspnet:10.0 runs as non-root `app` (APP_UID 1654); Kestrel binds 8080
# (ASPNETCORE_HTTP_PORTS from the base image — the quiet fallback; the
# comuki way is [server] port / COMUKI_SERVER_PORT, see deploy/README.md).
EXPOSE 8080

ENTRYPOINT ["/app/host/comuki"]
