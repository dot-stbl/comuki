## Context

See `proposal.md`. Sibling changes already specify env class, sandbox, secrets, editions (four points, explicitly *not* the coding-agent worker), and worker-pools. This design is the composition root for a slot, not a sixth copy of those behaviors.

## Goals / Non-Goals

**Goals:** One record, one ordered deny, one journal, four axes that cannot collapse into `comuki-worker:latest`.

**Non-Goals:** Capability Broker; bake pipeline; implementing the sibling gates.

## Decisions

### D1. Admission is a compute chokepoint, Broker is a semantic one

Broker authorizes "create task" / "scan repo". Admission authorizes "this slot may run". Both exist. Slot start MUST NOT call the Broker for every field — it consumes the already-admitted record.

### D2. Order is cheap-to-expensive

Unconfirmed class (row read) before capacity (pool) before secrets (resolver) before edition (license). Fail closed at the first miss so GPU miss never looks like a secret error.

### D3. 1:1 containers still admit

Do not wait for worker-pools. Today's container is a host with one slot. Admission wraps it so env+sandbox+edition cannot be skipped until pools exist.

### D4. Feature keys for GPU/Windows/community are registry rows

Do not hardcode edition logic in Translator. Paid class ⇒ `[RequiresFeature]` equivalent on the catalog entry, evaluated by `EditionGate`.

## Risks / Trade-offs

- **[Risk] Dual-write until Repositories land.** Admission reads `EnvClass` from Project scalar; must switch with the attachment migration.
- **[Risk] Five sibling changes in flight.** Admission interfaces should be ports (`ISlotAdmission`) so sandbox/secrets can land in either order.
- **[Trade-off] Over-structured vs five ifs.** Extra type; one place to audit.

## Migration Plan

Ship `ISlotAdmission` that always succeeds in Development with a default class if unset; Production fail-closed once `add-worker-environments` tracer binds this repo. Then tighten denials as sandbox/secrets/editions wire in.

## Open Questions

- Exact paid feature key names for Windows/GPU — registry rows at implementation, not spec blockers.
