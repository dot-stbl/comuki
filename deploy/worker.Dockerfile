# Comuki worker image — Slice 0 runtime (issue #4, T3.4).
#
# Multi-stage:
#   1. build  — SDK stage compiles Comuki.Host.Translator (the container CMD)
#   2. final  — oven/bun + pi (@earendil-works/pi-coding-agent via bun add -g)
#               + the published translator + ENTRYPOINT translator
#
# pi needs bun >= 1.4 (0.84.x crashes on 1.3.x with
# `webidl.util.markAsUncloneable is not a function` — T3.0 finding).
#
# The translator binary is framework-dependent .NET 10; the final stage
# installs the ASP.NET/Core runtime via the dotnet install script. The
# worker image intentionally carries no SDK.
#
# Sanity mode (no orchestrator): `podman run --rm --entrypoint pi <img> --version`
# Reference: comuki-slice-0.md § Шаг 0 / 05; scope-draft § 4 Runtime.

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

# The translator (container CMD) + its default worktree mount.
COPY --from=build /app /app/translator
WORKDIR /work
VOLUME /work

# COMUKI_ORCH_HTTP defaults to the orchestrator container on the compose
# network; the rest of the COMUKI_* contract is stamped at container start.
ENV COMUKI_ORCH_HTTP=http://comuki-host:8080 \
    COMUKI_ORCH_GRPC=http://comuki-host:5051 \
    COMUKI_PI_EXECUTABLE=pi

ENTRYPOINT ["dotnet", "/app/translator/Comuki.Host.Translator.dll"]
