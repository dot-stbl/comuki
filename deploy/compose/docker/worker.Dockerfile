# Comuki worker image — the container the compute engine spawns per
# work item. NOT a long-running service; the orchestrator starts and
# stops these.
#
# Contents (Translator contract, platform/src/host/Comuki.Host.Translator):
#   * comuki-translator (framework-dependent .NET 10 publish) — the
#     ENTRYPOINT: claims work over REST, runs the bidi gRPC worker
#     stream, spawns the agent per work item;
#   * pi (@earendil-works/pi-coding-agent via bun add -g) — the headless
#     agent runtime the Translator spawns;
#   * agents/ TS workspace (comuki-agent-core + comuki-worker-sdk) —
#     what pi loads as Comuki worker extensions.
#
# The orchestrator stamps the COMUKI_* contract on every spawned
# container (DockerComputeMapping / KubernetesComputeMapping):
#   COMUKI_ORCH_HTTP / COMUKI_ORCH_GRPC   orchestrator endpoints
#   COMUKI_WORKER_TOKEN                   opaque auth token
#   COMUKI_PROFILE_KEY / COMUKI_PROFILES_REF / COMUKI_WORKER_IMAGE
#                                         claim labels
#
# Build context = repo root:
#   docker build -f deploy/compose/docker/worker.Dockerfile \
#     -t comuki-worker:local .
#
# pi needs bun >= 1.4 (older bun crashes the agent runtime).

# ---------- Stage 1: build the translator ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first for layer caching: solution-wide pins + the translator graph.
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj platform/src/shared/Comuki.Shared.Kernel/
COPY platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj platform/src/shared/Comuki.Shared.Contracts/
COPY platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj platform/src/host/Comuki.Host.Translator/
RUN dotnet restore platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj -r linux-x64

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

# The translator (container ENTRYPOINT, apphost binary — issue #54) +
# its default worktree mount.
COPY --from=build /app /app/translator

# Comuki agents workspace: comuki-agent-core + comuki-worker-sdk sources
# and their dependencies, installed once at build time so spawned agent
# sessions can import them without network access.
COPY agents/package.json agents/tsconfig.base.json ./opt/comuki/agents/
COPY agents/comuki-agent-core /opt/comuki/agents/comuki-agent-core
COPY agents/comuki-worker-sdk /opt/comuki/agents/comuki-worker-sdk
RUN cd /opt/comuki/agents && bun install

# Defaults point at the compose service name so a manually spawned
# container works out of the box; the orchestrator overrides these.
ENV COMUKI_ORCH_HTTP=http://comuki-host:8080 \
    COMUKI_ORCH_GRPC=http://comuki-host:8080 \
    COMUKI_PI_EXECUTABLE=pi

WORKDIR /work
VOLUME /work

ENTRYPOINT ["/app/translator/comuki-translator"]
