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
#     --build-arg COMUKI_VERSION=0.1.0 \
#     -t comuki-worker:local .
#
# Build args:
#   COMUKI_VERSION  Stamped into the translator via -p:VersionPrefix
#                   (surfaced by `comuki-translator version`). Default
#                   0.0.0 = unstamped local build. The host image of the
#                   same release MUST carry the same value — the worker
#                   spawn pinning derives its tag from it (RELEASE.md).
#   PI_VERSION      @earendil-works/pi-coding-agent version, pinned so a
#                   release tag is reproducible (matches the vendored
#                   version of the hybrid contour). Bump deliberately.
#
# pi needs bun >= 1.4 (older bun crashes the agent runtime).

# ---------- Stage 1: build the translator ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Release stamping: 0.0.0 = unstamped local build.
ARG COMUKI_VERSION=0.0.0

WORKDIR /src

# Restore first for layer caching: solution-wide pins + the translator graph.
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj platform/src/shared/Comuki.Shared.Kernel/
COPY platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj platform/src/shared/Comuki.Shared.Contracts/
COPY platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj platform/src/host/Comuki.Host.Translator/
RUN dotnet restore platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj -r linux-x64

COPY platform/ platform/
RUN dotnet publish platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj \
    -c Release -r linux-x64 --no-restore -p:VersionPrefix=${COMUKI_VERSION} -o /app

# ---------- Stage 2: the worker ----------
FROM oven/bun:1.4.0-slim

# Pinned so a release tag rebuilds bit-comparably; keep in sync with the
# hybrid contour's vendored tarball (deploy/hybrid/worker.Dockerfile).
ARG PI_VERSION=0.85.1

# pi-coding-agent is the headless agent runtime the translator spawns.
RUN bun add -g @earendil-works/pi-coding-agent@${PI_VERSION}

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

# Non-root worker (worker-sandbox hardening defaults): the agent process
# must never run as uid 0. /work (the volume root; docker copies its
# ownership into fresh volumes) and the agents workspace are chowned so
# translator/pi/bun can write; the translator tree stays root-owned r-x.
# pi is installed under bun's global prefix as root — expose it on the
# system PATH so the non-root user can exec it regardless of prefix.
RUN groupadd --gid 1000 comuki \
    && useradd --uid 1000 --gid 1000 --create-home --shell /bin/sh comuki \
    && chown -R 1000:1000 /work /opt/comuki \
    && ln -sf "$(bun pm -g bin)/pi" /usr/local/bin/pi

USER 1000:1000

ENTRYPOINT ["/app/translator/comuki-translator"]
