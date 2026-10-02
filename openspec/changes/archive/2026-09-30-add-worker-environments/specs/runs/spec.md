## MODIFIED Requirements

### Requirement: Work item claim shape
A work item SHALL carry the claim labels `ProfileKey`, **`EnvClass`**, `ProfilesRef` (worker matches on all three), a raw-JSON `Brief`, the lease columns `LeasedBy` / `LeaseUntil` / `HeartbeatAt`, and an `Attempt` counter that counts claims including requeue retries. Creation SHALL require non-empty profile key, **confirmed env class**, profiles ref and brief. The env class SHALL be copied from the target repository's `EnvClass` at enqueue; Brain SHALL NOT supply a different class. Assigning a lease SHALL be legal only from `Queued`, SHALL bump `Attempt` and SHALL move the item to `Running` (see work-queue for the SQL contract).

#### Scenario: Attempt counts claims
- **WHEN** an item is claimed, reaped, requeued and claimed again
- **THEN** its `Attempt` is 2

#### Scenario: Enqueue copies repository class
- **WHEN** a run creates a work item whose target repository binds `net10-sdk-bun`
- **THEN** the work item's `EnvClass` is `net10-sdk-bun` regardless of any image string on the Project

## ADDED Requirements

### Requirement: Restore journal events
The `run_events` journal SHALL record `worker.restore_started` when Translator begins class restore opcodes and `worker.restore_failed` when an opcode exits non-zero (payload: opcode name, exit code; no secret values). Successful restore MAY be implied by `worker.reported` StageStart and SHALL NOT require a third type.

#### Scenario: Restore failure is on the timeline
- **WHEN** `dotnet restore` exits non-zero
- **THEN** the run journal contains `worker.restore_failed` and pi is not started
