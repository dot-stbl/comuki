# Comuki worker image — Phase 4 (Slice 0 step 0).
#
# Minimal pi-coding-agent container for the headless sanity check.
# Multi-stage not needed yet — Translator (C# AOT-deferred regular Worker)
# lands in 04-03 and will trigger a two-stage build at that point.
#
# Contents:
#   - oven/bun:1.3.10-bookworm-slim base (bun already in the image)
#   - @earendil-works/pi-coding-agent installed globally via bun
#   - /work as the default worktree mount for future stages
#
# Reference: comuki-architecture.md § 03 (Управляющий цикл),
#            comuki-slice-0.md § Шаг 0 (Sanity-check pi).

FROM oven/bun:1.3.10-slim

LABEL comuki.worker.phase="4-prep" \
      comuki.worker.translator="deferred-to-04-03" \
      comuki.worker.replaces="sentinel-from-phase-3"

# pi-coding-agent is the headless runtime we'll use as the worker agent.
# Correct npm package name: @earendil-works/pi-coding-agent (NOT
# @pi-coding/agent, which the phase-3 sentinel incorrectly listed).
RUN bun add -g @earendil-works/pi-coding-agent

# /work is the worktree mount point for future task execution (04-05).
# In Phase 4 prep / 04-01 it's just the default working dir for the prompt.
WORKDIR /work
VOLUME /work

# ENTRYPOINT is `pi` so the container is invocable as
# `podman run --rm comuki/worker:dev -p "..." --output-format stream-json`.
# CMD is unused but kept as a sane default (the test script always passes
# the prompt explicitly).
ENTRYPOINT ["pi"]
CMD ["--help"]
