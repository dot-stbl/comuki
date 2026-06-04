# Comuki worker image — placeholder for Phase 3 (Slice 0 Step 2).
#
# What this image will be once the worker lands:
#   - AOT-built Translator (C#) as the entrypoint: `Process.Start(pi)`,
#     parse stream-json, gRPC bidirectional stream to Orchestrator.
#   - pi-coding-agent (Node.js) invoked by Translator as a child process.
#   - Worktree mount point for the agent to read/write code.
#   - Healthcheck endpoint that pings the Orchestrator gRPC channel.
#
# For now this is a sentinel that fails the build fast with a clear
# message — anyone trying to `docker build -f deploy/worker.Dockerfile`
# before Phase 3 lands gets told exactly why. Replaced in Phase 3.
#
# Reference (comuki-architecture.md § 03 + comuki-stack.md § 03):
#   - Translator: Comuki.Platform.Worker.Translator (C#, AOT)
#   - Agent runtime: pi-coding-agent
#   - Channel: gRPC bidi (comuki-decisions.md § "Транспорты по природе шва")
#
# Future shape (do NOT build yet, will land in Phase 3 PR):
#
#   FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
#   WORKDIR /src
#   COPY platform/src/worker/Comuki.Platform.Worker.Translator/ ./
#   RUN dotnet publish -c Release -r linux-x64 --self-contained \
#       /p:PublishAot=true -o /out
#
#   FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
#   RUN apt-get update && apt-get install -y --no-install-recommends \
#       nodejs npm git curl ca-certificates && \
#       npm install -g @pi-coding/agent && \
#       rm -rf /var/lib/apt/lists/*
#   COPY --from=build /out/Comuki.Platform.Worker.Translator /usr/local/bin/translator
#   WORKDIR /work
#   VOLUME /work
#   ENTRYPOINT ["/usr/local/bin/translator"]
#   EXPOSE 5000
#   HEALTHCHECK CMD curl -fsS http://localhost:5000/health || exit 1

FROM scratch
LABEL comuki.worker="phase-3-skeleton-not-yet-implemented" \
      comuki.worker.replaces="comuki-decisions.md § Translator" \
      comuki.worker.target="Comuki.Platform.Worker.Translator (C# AOT)"
