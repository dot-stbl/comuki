# Comuki.TestFakeModel image — WS5 (issue #134): the standalone fake/replay/
# record model server, packaged so it can sit in deploy/compose.e2e.yml (T3)
# as the worker's ANTHROPIC_BASE_URL/OPENAI_BASE_URL upstream (design.md:
# "a published container image (comuki-test-fake-model:latest) for
# deploy/compose.e2e.yml (T3), so the same binary backs every tier").
#
# Multi-stage: SDK stage publishes the tool (framework-dependent — the
# runtime stage below carries the ASP.NET Core shared framework, no SDK);
# runtime stage just runs it. mcr.microsoft.com/dotnet/aspnet:10.0 already
# runs as non-root `app` (APP_UID 1654), same as deploy/hybrid/host.Dockerfile.
#
# Everything Program.cs reads as a --flag is also a COMUKI_TESTFAKEMODEL_*
# env var (a flag always wins) — see Program.cs's Main doc for the full
# list. Defaults below give a working `fake` container with zero extra
# config: `podman run --rm -p 17190:17190 comuki-test-fake-model:local`.
#
# Build-context note: this Dockerfile COPYs the repo-root pin files
# (Directory.Build.props/Directory.Packages.props/nuget.config/.editorconfig
# — Comuki.TestFakeModel has no ProjectReferences, so nothing else from the
# repo is needed) alongside its own project directory, so build with the
# REPO ROOT as context. The repo's own .dockerignore excludes `tests/`
# wholesale (it's written for the repo-root-context images under deploy/,
# which never need test-tool sources) — this image is the one exception,
# so build it with the sibling .containerignore override:
#   podman build --ignorefile tests/tools/Comuki.TestFakeModel/.containerignore \
#     -f tests/tools/Comuki.TestFakeModel/Dockerfile \
#     -t comuki-test-fake-model:local .

# ---------- Stage 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first for layer caching.
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY tests/tools/Comuki.TestFakeModel/ tests/tools/Comuki.TestFakeModel/
RUN dotnet restore tests/tools/Comuki.TestFakeModel/Comuki.TestFakeModel.csproj

RUN dotnet publish tests/tools/Comuki.TestFakeModel/Comuki.TestFakeModel.csproj \
    -c Release --no-restore -o /app

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=build /app /app

# `fake` needs no script/cassette — it serves the bundled Fixtures/hello.fake.json.
ENV COMUKI_TESTFAKEMODEL_MODE=fake \
    COMUKI_TESTFAKEMODEL_PORT=17190

EXPOSE 17190

ENTRYPOINT ["/app/Comuki.TestFakeModel"]
