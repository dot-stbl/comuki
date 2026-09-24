## Why

Chat sessions (Voluta graph), Memory (facts/digest/learning candidates), and
Brain tool wiring already shipped under S5. Main `openspec/specs/` never
received the capability contracts; only the earlier design-only
`add-chat-memory` change exists. This backfill records the landed behavior.

## What Changes

- Add main capabilities `chat` and `memory` describing today's REST/session
  surface, persistence, digest, and brain `memory.search`.
- Cross-link host composition notes where the empty digest stub still stands
  in for a wired MemoryDigest.
- Leave learning-candidate human-approve → client-git as deferred (table
  exists; approval pipeline not the v0 bar).

## Capabilities

### New Capabilities
- `chat`: session lifecycle, Voluta turns, slash catalog, subject ownership
- `memory`: facts + checkpoints/messages schema, digest, sweep, brain search

### Modified Capabilities
- (none — host already documents composition; chat/memory are new folders)

## Impact

Docs only. Code already in `platform/src/modules/{Chat,Memory}` and
`Comuki.Host{,.Brain}/Chat` / Brain toolbox.

## Non-goals

- Replacing EmptyMemoryDigest with full digest wiring (tracked separately)
- Learning-candidate approve → PR automation
- Streaming token UI over SignalR for chat turns
