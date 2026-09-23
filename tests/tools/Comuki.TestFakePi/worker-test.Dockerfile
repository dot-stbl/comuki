# =============================================================================
# comuki TEST worker image — add-agentic-test-contour WS6 (T2a).
#
# NOT a production artifact: this is a throwaway image the
# Comuki.EndToEnd.AgentLoop suite builds locally to prove the real
# container-lifecycle path (Comuki.Engine.Compute.Docker provisioning a
# real container -> gRPC bidi stream -> journal), with TestFakePi standing
# in for `pi` so no model call is ever made (T2a, design.md D3). It is a
# deliberately smaller sibling of deploy/hybrid/worker.Dockerfile — same
# ENTRYPOINT contract (comuki-translator), same donor-image strategy, but
# with pi/bun/the agents workspace dropped entirely: TestFakePi is a
# self-contained .NET console app, it needs none of that. Do not point any
# real deployment at this image.
#
# COMUKI_PI_EXECUTABLE is the exact seam this reuses — already a first-class
# option on Comuki.Host.Translator's TranslatorOptions ("Production: pi;
# tests: TestFakePi") — no Translator/Compute production code changes were
# needed to wire this up.
#
# Build (context = repo root):
#   podman build -f tests/tools/Comuki.TestFakePi/worker-test.Dockerfile \
#     -t comuki-agent-test-worker:ws6 .
# Sanity (no orchestrator): `podman run --rm --entrypoint /app/testfakepi/Comuki.TestFakePi <img> -p hi --mode json --no-session`
# =============================================================================

# ---------- Stage 1: build the translator + TestFakePi ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first for layer caching — Comuki.Host.Translator's own direct
# ProjectReferences only (Bootstrap/Kernel/Contracts; it never references
# the engine projects), plus TestFakePi (zero ProjectReferences of its own).
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY platform/src/shared/Comuki.Shared.Bootstrap/Comuki.Shared.Bootstrap.csproj platform/src/shared/Comuki.Shared.Bootstrap/
COPY platform/src/shared/Comuki.Shared.Kernel/Comuki.Shared.Kernel.csproj platform/src/shared/Comuki.Shared.Kernel/
COPY platform/src/shared/Comuki.Shared.Contracts/Comuki.Shared.Contracts.csproj platform/src/shared/Comuki.Shared.Contracts/
COPY platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj platform/src/host/Comuki.Host.Translator/
COPY tests/tools/Comuki.TestFakePi/Comuki.TestFakePi.csproj tests/tools/Comuki.TestFakePi/
RUN dotnet restore platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj -r linux-x64 \
    && dotnet restore tests/tools/Comuki.TestFakePi/Comuki.TestFakePi.csproj -r linux-x64

# Build and publish both — framework-dependent apphosts (--self-contained
# false explicit, not left to the SDK's RID-triggered default), so the
# shared runtime copied into the final stage below is load-bearing, not
# vestigial.
COPY platform/ platform/
COPY tests/tools/Comuki.TestFakePi/ tests/tools/Comuki.TestFakePi/
RUN dotnet publish platform/src/host/Comuki.Host.Translator/Comuki.Host.Translator.csproj \
      -c Release -r linux-x64 --no-restore --self-contained false -o /app/translator \
    && dotnet publish tests/tools/Comuki.TestFakePi/Comuki.TestFakePi.csproj \
      -c Release -r linux-x64 --no-restore --self-contained false -o /app/testfakepi

# ---------- Stage 2: donor image for the .NET runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS dotnet

# ---------- Stage 3: the test worker ----------
# buildpack-deps:bookworm-scm — same final base as deploy/hybrid/worker.Dockerfile,
# so curl/git/ca-certificates are present (curl backs
# Comuki.AgentTest.Runner.Compute.ContainerHostAddressResolver's probe).
FROM docker.io/library/buildpack-deps:bookworm-scm

COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

COPY --from=build /app/translator /app/translator
COPY --from=build /app/testfakepi /app/testfakepi

# TestFakePi replaces pi entirely — no model call is ever made (T2a).
# COMUKI_ORCH_HTTP/COMUKI_ORCH_GRPC are placeholders here; the real values
# are stamped per-start by DockerComputeMapping.BuildEnvironment
# (COMUKI_ORCH_GRPC) and Comuki.EndToEnd.AgentLoop's ComputeStartRequest.Env
# (COMUKI_ORCH_HTTP), same contract deploy/hybrid/worker.Dockerfile documents.
ENV COMUKI_ORCH_HTTP=http://unset.invalid:8080 \
    COMUKI_ORCH_GRPC=http://unset.invalid:8080 \
    COMUKI_PI_EXECUTABLE=/app/testfakepi/Comuki.TestFakePi

# Non-root (worker-sandbox hardening default — matches
# deploy/compose/docker/worker.Dockerfile's posture): /work is the
# translator's spawned-process working directory and must be writable;
# /app stays root-owned r-x.
RUN groupadd --gid 1000 comuki \
    && useradd --uid 1000 --gid 1000 --create-home --shell /bin/sh comuki \
    && mkdir -p /work \
    && chown -R 1000:1000 /work

USER 1000:1000
WORKDIR /work
VOLUME /work

ENTRYPOINT ["/app/translator/comuki-translator"]
