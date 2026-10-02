## Context

#47 deferred a verifier container because `GenericCommandVerifierWorker` uses `Process.Start` on the Host. Env classes with SDKs make in-process verify an isolation bug, not a convenience. See `add-worker-environments` and `add-worker-admission`.

## Goals / Non-Goals

**Goals:** Same class as the item; no compiler next to postgres; structured verify journal.

**Non-Goals:** Full CI; watch/dev servers; replacing pi implement.

## Decisions

### D1. Reuse class image, optional no-pi entry

The golden bundle already has SDK+bun+translator. Verify can be Translator with a "verify-only" loop (restore + opcodes) or a second ENTRYPOINT. Prefer Translator loop flag to avoid a second image.

### D2. Admission is mandatory

Verify slots go through SlotAdmission so Community/GPU/sandbox rules apply. A verify that bypassed admission would be the new Host `Process.Start`.

### D3. Recover #47 intent, not the rescue branch blindly

`rescue/generic-command-verifier` is a prior implementation. Port the command list/report shape if it matches; do not restore in-process `Process.Start`.

## Risks / Trade-offs

- **[Risk] Cold slot per verify.** → Warm pool of the class (worker-pools) later; tracer may be 1:1 container.
- **[Trade-off] Duplicate restore if implement already restored.** → Accept for tracer; cache volume keyed by lock hash (env spec).

## Migration Plan

Gate Host runner behind an options flag defaulting to isolated in Production; Development may keep in-process until the slot path is green. Then delete the Host runner.

## Open Questions

- Exact verify opcode table per class (`dotnet build` vs `dotnet run --project tests/...`) — belongs in `.comuki` schema v2, not a blocker for isolation.
