## ADDED Requirements

### Requirement: WorkerFeatureGate is the first paid-feature consumer

The existing `WorkerFeatureGate` (with deferred-start semantics) SHALL become the first real consumer of `[RequiresFeature]` / `[EnforceLimit]` for a paid feature. The five paid orchestra keys (`steering`, `critic`, `verification`, `automation`, `model-control`) gain their first `WorkerFeatureGate` / `[RequiresFeature]` / `[EditionFeature]` call-sites as each phase ships (see `editions` capability for the registry contract).

> **Coordination note (2026-10-04).** The first consumer is `CriticSweepWorker` with `[RequiresFeature(Features.Critic)]` (the `critic` phase — the same phase introduces the `Critic` feature key in the editions registry, so the call-site and the key land together; the architecture test `EveryPaidRegistryEntryIsGatedShould` stays green). The other four keys' first consumers ship in their owning phases (see `editions/spec.md` §3).

#### Scenario: WorkerFeatureGate filters the registry at startup

- **WHEN** a Community deployment boots with `CriticSweepWorker` registered
- **THEN** `ComukiWorkerRegistry.Snapshot()` does not include `CriticSweepWorker`; no loop spins up for it; the registry logs a `Warning` naming the worker and the missing feature key

#### Scenario: Paid deployment includes the worker

- **WHEN** a paid deployment boots with `CriticSweepWorker` registered
- **THEN** `ComukiWorkerRegistry.Snapshot()` includes `CriticSweepWorker` and the worker loop starts on the configured schedule

### Requirement: New permissions land with the orchestra phases

The orchestra phases introduce six new permission keys in `Permissions.cs` and `RoleMatrix` (the canonical model-control keys are the `model-control:read` / `model-control:write` pair — not the existing `proxy:write`, which is the surface for revoke / mint / revoke-mint operations on the existing proxy key store and is independent of model tuning; see the `model-control` capability for the canonical access model):

| Permission | Granted to (initial) |
|---|---|
| `observability:read` | `PlatformAdmin`, `Operator`, `ProjectAdmin`, `Member` |
| `automation:read` | `PlatformAdmin`, `Operator`, `ProjectAdmin`, `Member` |
| `automation:write` | `PlatformAdmin`, `Operator`, `ProjectAdmin` (admin-only, like `scheduler:write`) |
| `model-control:read` | `PlatformAdmin`, `Operator`, `ProjectAdmin`, `Member` |
| `model-control:write` | `PlatformAdmin`, `Operator`, `ProjectAdmin` |
| `platform:write` | `PlatformAdmin`, `Operator` (orchestra write path) |

The existing `PermissionDemandStartupValidator` enforces that every demanded key is declared in the matrix. The new keys follow the same pattern.

#### Scenario: Startup validator accepts the new keys

- **WHEN** the host boots with the new permission keys demanded by the orchestra handlers
- **THEN** the existing `PermissionDemandStartupValidator` accepts the change and the build passes

#### Scenario: Member-level write is denied

- **WHEN** a caller with role `Member` (read-only) attempts `PATCH /api/v1/projects/{projectId}/automations/{id}`
- **THEN** the response is `403` with `code = automation.permission_denied`

#### Scenario: Project-admin write is granted

- **WHEN** a caller with role `ProjectAdmin` attempts the same PATCH
- **THEN** the response is `200` with the updated view

### Requirement: Existing permission patterns stay

The `PermissionDemandStartupValidator`, the `RoleMatrix` catalog-count test, and the "every key held by a role" test pattern are unchanged. The five new keys extend the matrix without breaking the existing contract.

#### Scenario: Every key is held by a role

- **WHEN** `dotnet run --project tests/unit/Comuki.Modules.Identity.Unit` runs after the new keys are added
- **THEN** `RoleMatrixShould` reports the same row count plus the new keys, and "every key held by a role" passes

## ADAPTER Notes

`Permissions.cs` and `RoleMatrix.cs` are the existing composition surfaces; this change adds rows without forking the pattern. `WorkerFeatureGate` ships on master with consumers — `ComukiWorkerRegistry.PartitionWorkers` reads it (the partitioning is the gate-aware entry point), and `ComukiWorkerRegistry.Snapshot()` is the read-side surface (`BackgroundWorkersEndpoints.cs:24` calls it on every status read) — but no registered worker on master carries `[RequiresFeature]`, so the partitioning always produces the same "all workers" output. This change is the first to land a worker that carries the attribute (`CriticSweepWorker` with `[RequiresFeature(Features.Critic)]`); the partitioning then becomes load-bearing for the first time. The five paid keys follow the existing `required-staff-checked` architecture test (`PermissionDemandStartupValidator`) and the existing role-matrix tests; no new test surface.