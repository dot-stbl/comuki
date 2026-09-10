# =============================================================================
# comuki worker image — the container the compute engine spawns per claim.
#
# Contents (Translator contract, platform/src/host/Comuki.Host.Translator):
#   * Comuki.Host.Translator (framework-dependent .NET 10 publish) — the
#     ENTRYPOINT: claims work over REST, runs the bidi gRPC stream, spawns
#     the agent per work item;
#   * pi (@earendil-works/pi-coding-agent via bun add -g) — the headless
#     agent runtime the Translator spawns (COMUKI_PI_EXECUTABLE, default
#     `pi`);
#   * agents/ TS workspace (comuki-agent-core + comuki-worker-sdk source +
#     dependencies) — what pi loads as Comuki worker extensions in later
#     slices; installed now so the image is self-contained.
#
# COMUKI_* contract (stamped per claim by KubernetesComputeMapping /
# DockerComputeMapping, overridable at spawn):
#   COMUKI_ORCH_HTTP    orchestrator REST (claim/heartbeat/complete/fail)
#   COMUKI_ORCH_GRPC    orchestrator gRPC (bidi worker stream)
#   COMUKI_WORKER_TOKEN worker auth token
#   COMUKI_PROFILE_KEY / COMUKI_PROFILES_REF / COMUKI_WORKER_IMAGE  claim labels
#   COMUKI_PI_EXECUTABLE  pi entrypoint (default `pi`)
#   COMUKI_PROFILES_PATH / COMUKI_PROFILES_GIT_URL  client profiles source
#
# Defaults below point at the in-cluster service of the dev release
# (releaseName `comuki`, ns `comuki`) so a manually spawned pod works.
#
# pi needs bun >= 1.4 (0.84.x crashes on 1.3.x — see deploy/worker.Dockerfile
# history, T3.0 finding). Sanity: `podman run --rm --entrypoint pi <img> --version`.
# =============================================================================

# ---------- Stage 1: build the translator ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first for layer caching: solution-wide pins + the translator graph.
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj platform/src/shared/Comuki.Shared.Kernel/
COPY platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj platform/src/shared/Comuki.Shared.Contracts/
COPY platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj platform/src/host/Comuki.Host.Translator/
RUN dotnet restore platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj -r linux-x64

# Build and publish.
COPY platform/ platform/
RUN dotnet publish platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj \
    -c Release -r linux-x64 --no-restore -o /app

# ---------- Stage 2: the worker ----------
FROM oven/bun:1.4.0-slim

# pi-coding-agent is the headless agent runtime the translator spawns.
RUN bun add -g @earendil-works/pi-coding-agent

# .NET 10 runtime for the translator (no SDK in the worker image).
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    # slim base has no libicu; the translator does no culture-sensitive work
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# The translator (container ENTRYPOINT) + its default worktree mount.
COPY --from=build /app /app/translator

# Comuki agents workspace: comuki-agent-core + comuki-worker-sdk sources and
# their dependencies (zod), installed once at build time so spawned pi
# sessions can import them without network access.
COPY agents/package.json agents/tsconfig.base.json ./opt/comuki/agents/
COPY agents/comuki-agent-core /opt/comuki/agents/comuki-agent-core
COPY agents/comuki-worker-sdk /opt/comuki/agents/comuki-worker-sdk
RUN cd /opt/comuki/agents && bun install

ENV COMUKI_ORCH_HTTP=http://comuki:8080 \
    COMUKI_ORCH_GRPC=http://comuki:8080 \
    COMUKI_PI_EXECUTABLE=pi

WORKDIR /work
VOLUME /work

ENTRYPOINT ["dotnet", "/app/translator/Comuki.Host.Translator.dll"]
