# =============================================================================
# comuki host image — one image, two entrypoints (console.x pattern):
#   /app/host      Comuki.Host       (this Deployment via chart command)
#   /app/migrator  Comuki.Migrator   (batch Job, deploy/hybrid/migrate-job-dev.yaml)
#
# Build context = repo root (kaniko: KANIKO_DOCKERFILE=deploy/hybrid/
# host.Dockerfile, context .). nuget.config restores from nuget.org only —
# no private feed tokens needed inside the build.
#
# .NET 10, non-root `app` (APP_UID 1654) — matches securityContext in
# deploy/hybrid/dev.yaml (chart default).
# =============================================================================

# ---------- Stage 1: build host + migrator ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first for layer caching: repo-wide pins + the two host graphs
# (their transitive ProjectReferences pull in every module).
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/ platform/
RUN dotnet restore platform/src/host/Comuki.Host/Comuki.Host.csproj \
    && dotnet restore platform/src/host/Comuki.Migrator/Comuki.Migrator.csproj

# Publish each host into its own directory: separate dependency closures,
# one shared image — versions between entrypoints cannot diverge.
RUN dotnet publish platform/src/host/Comuki.Host/Comuki.Host.csproj \
    -c Release --no-restore -o /app/host \
    && dotnet publish platform/src/host/Comuki.Migrator/Comuki.Migrator.csproj \
    -c Release --no-restore -o /app/migrator

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=build /app/host /app/host
COPY --from=build /app/migrator /app/migrator

# aspnet:10.0 already runs as non-root `app` (APP_UID 1654) and Kestrel
# binds 8080 by default. chart values set workingDir /app/host + command
# [dotnet, Comuki.Host.dll].
EXPOSE 8080

ENTRYPOINT ["dotnet", "Comuki.Host.dll"]
WORKDIR /app/host
