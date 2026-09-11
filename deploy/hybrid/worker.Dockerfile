# =============================================================================
# comuki worker image — the container the compute engine spawns per claim.
#
# Contents (Translator contract, platform/src/host/Comuki.Host.Translator):
#   * Comuki.Host.Translator (framework-dependent .NET 10 publish) — the
#     ENTRYPOINT: claims work over REST, runs the bidi gRPC stream, spawns
#     the agent per work item;
#   * pi (@earendil-works/pi-coding-agent 0.85.1, vendored tarball) — the
#     headless agent runtime the Translator spawns (COMUKI_PI_EXECUTABLE,
#     default `pi`);
#   * agents/ TS workspace (comuki-agent-core + comuki-worker-sdk source
#     + vendored zod) — what pi loads as Comuki worker extensions in later
#     slices; installed now so the image is self-contained.
#
# Offline-build policy (pipeline #13 lesson): the nova kaniko runner reaches
# mcr.microsoft.com, docker.io and nuget.org, but NOT registry.npmjs.org —
# `bun add -g` hung 40+ minutes and had to be cancelled. Therefore this
# image build NEVER touches npm and never runs apt-get:
#   - pi + zod are vendored tarballs from deploy/hybrid/vendor/ (bump by
#     re-running `npm pack <pkg>@<ver> --pack-destination deploy/hybrid/vendor`
#     and updating the COPY lines);
#   - bun is copied from the oven/bun image (docker.io);
#   - the .NET 10 runtime is copied from the official aspnet image (mcr);
#   - git/curl/ca-certificates/openssh-client ship with buildpack-deps.
# pi's npm bin is a self-contained bundle (dist/bundle/cli.js with relative
# chunks only — no node_modules needed), so extracting the tarball and
# wrapping it with `exec bun ...` replaces `bun add -g` entirely.
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

# ---------- Stage 1: build the translator (mcr + nuget.org — runner-proven) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first for layer caching: solution-wide pins + the translator graph.
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/src/shared/Comuki.Shared.Bootstrap/Comuki.Shared.Bootstrap.csproj platform/src/shared/Comuki.Shared.Bootstrap/
COPY platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj platform/src/shared/Comuki.Shared.Kernel/
COPY platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj platform/src/shared/Comuki.Shared.Contracts/
COPY platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj platform/src/host/Comuki.Host.Translator/
RUN dotnet restore platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj -r linux-x64

# Build and publish.
COPY platform/ platform/
RUN dotnet publish platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj \
    -c Release -r linux-x64 --no-restore -o /app

# ---------- Stage 2: donor images for bun + the .NET runtime ----------
# oven/bun via docker.io and aspnet via mcr — both pulled successfully by
# pipeline #13, no unproven hosts involved.
FROM oven/bun:1.4.0-slim AS bun
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS dotnet

# ---------- Stage 3: the worker ----------
# buildpack-deps:bookworm-scm (docker.io) ships git, curl, ca-certificates
# and openssh-client — everything the old `apt-get install` provided,
# without touching debian mirrors. Same Debian release as oven/bun slim,
# so the copied bun binary's glibc matches.
FROM docker.io/library/buildpack-deps:bookworm-scm

# bun runtime for pi (>= 1.4 required).
COPY --from=bun /usr/local/bin/bun /usr/local/bin/bun
COPY --from=bun /usr/local/bin/bunx /usr/local/bin/bunx
ENV BUN_INSTALL=/usr/local

# .NET 10 runtime for the translator (no SDK in the worker image).
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    # no libicu guarantee on this base; the translator does no
    # culture-sensitive work
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# pi from the vendored tarball — registry-free. The wrapper script is what
# `bun add -g` would have given us: a bin shim that pins the interpreter.
COPY deploy/hybrid/vendor/earendil-works-pi-coding-agent-0.85.1.tgz /tmp/pi.tgz
RUN mkdir -p /opt/comuki/pi \
    && tar -xzf /tmp/pi.tgz -C /opt/comuki/pi --strip-components=1 \
    && rm /tmp/pi.tgz \
    && printf '#!/bin/sh\nexec bun /opt/comuki/pi/dist/bundle/cli.js "$@"\n' > /usr/local/bin/pi \
    && chmod +x /usr/local/bin/pi \
    && timeout 180 pi --version </dev/null

# The translator (container ENTRYPOINT) + its default worktree mount.
COPY --from=build /app /app/translator

# Comuki agents workspace: comuki-agent-core + comuki-worker-sdk sources.
# zod (their only external runtime dep — dependency-free itself) is vendored
# too; the workspace links are created by hand because `bun install` would
# resolve @types/bun/typescript/zod against registry.npmjs.org.
COPY agents/package.json agents/tsconfig.base.json ./opt/comuki/agents/
COPY agents/comuki-agent-core /opt/comuki/agents/comuki-agent-core
COPY agents/comuki-worker-sdk /opt/comuki/agents/comuki-worker-sdk
COPY deploy/hybrid/vendor/zod-4.5.4.tgz /tmp/zod.tgz
RUN cd /opt/comuki/agents \
    && mkdir -p node_modules/zod node_modules/@comuki \
    && tar -xzf /tmp/zod.tgz -C node_modules/zod --strip-components=1 \
    && rm /tmp/zod.tgz \
    && ln -s ../../comuki-agent-core node_modules/@comuki/agent-core \
    && ln -s ../../comuki-worker-sdk node_modules/@comuki/worker-sdk \
    && bun -e "await import('zod'); await import('@comuki/agent-core'); await import('@comuki/worker-sdk')"

ENV COMUKI_ORCH_HTTP=http://comuki:8080 \
    COMUKI_ORCH_GRPC=http://comuki:8080 \
    COMUKI_PI_EXECUTABLE=pi

WORKDIR /work
VOLUME /work

ENTRYPOINT ["/app/translator/comuki-translator"]
