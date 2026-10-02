# Comuki worker image — Golden bundle `net10-sdk-bun` (implement default).
#
# Part of openspec/changes/add-worker-environments, group 1 (issue #121,
# task 1.1). The historic runtime-only image (`deploy/worker.Dockerfile`)
# is NOT the implement default — net10-sdk-bun is, for any project bound
# to this environment class. This file is the catalog environment-class
# bundle (toolchain variant): same translator-publish stage as
# `worker.Dockerfile`, but the FINAL stage ships the FULL .NET 10 SDK
# (not just the runtime) so implement work can `dotnet restore` /
# `dotnet build` / run EF migrations / refresh solution-wide pins inside
# the container. The restore / build is driven by Translator restore
# opcodes declared in `.comuki/environment.toml` (see group 4 of the
# change), NOT by an agent `Bash` call.
#
# Multi-stage:
#   1. build  — SDK stage compiles the translator (the container CMD)
#   2. final  — oven/bun + pi (@earendil-works/pi-coding-agent via bun add -g)
#               + the published translator + the FULL .NET 10 SDK
#               (channel 10.0, no --runtime flag = SDK install) +
#               git + ENTRYPOINT comuki-translator
#
# pi needs bun >= 1.4 (0.84.x crashes on 1.3.x with
# `webidl.util.markAsUncloneable is not a function` — T3.0 finding).
#
# The translator binary (comuki-translator, issue #54) is
# framework-dependent .NET 10. This image also carries the matching
# .NET 10 SDK so implement work can build inside it; the runtime-only
# `deploy/worker.Dockerfile` is kept for non-implement workers (claim
# paths that run inside a pre-built repo).
#
# Sanity mode (no orchestrator): override the entrypoint to surface
# `dotnet --version` and `bun --version`:
#   podman run --rm --entrypoint /bin/sh deploy/env/net10-sdk-bun:dev \
#       -c "dotnet --version && bun --version"
# Reference: openspec/changes/add-worker-environments/tasks.md §1.

# ---------- Stage 1: build the translator ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first for layer caching: solution-wide pins + the translator graph.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj platform/src/shared/Comuki.Shared.Kernel/
COPY platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj platform/src/shared/Comuki.Shared.Contracts/
COPY platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj platform/src/host/Comuki.Host.Translator/
RUN dotnet restore platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj -r linux-x64

# Build and publish.
COPY platform/ platform/
RUN dotnet publish platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj \
    -c Release -r linux-x64 --no-restore -o /app

# ---------- Stage 2: the implement-default worker ----------
FROM oven/bun:1.4.0-slim

# pi-coding-agent is the headless agent runtime the translator spawns.
RUN bun add -g @earendil-works/pi-coding-agent

# FULL .NET 10 SDK (channel 10.0, no --runtime flag — dotnet-install
# defaults to SDK when --runtime is absent). This is the only delta vs
# the runtime-only `deploy/worker.Dockerfile`: that file passes
# `--runtime dotnet`, this one does NOT.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    # slim base has no libicu; the translator does no culture-sensitive work
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# The translator (container CMD) + its default worktree mount.
COPY --from=build /app /app/translator
WORKDIR /work
VOLUME /work

# COMUKI_ORCH_HTTP defaults to the orchestrator container on the compose
# network; the rest of the COMUKI_* contract is stamped at container start.
ENV COMUKI_ORCH_HTTP=http://comuki-host:8080 \
    COMUKI_ORCH_GRPC=http://comuki-host:8080 \
    COMUKI_PI_EXECUTABLE=pi

ENTRYPOINT ["/app/translator/comuki-translator"]
