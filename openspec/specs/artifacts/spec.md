# Artifacts Specification

## Purpose

Defines the run-artifact bundle: an immutable, per-run object set
(`brief.json` / `result.json` / `pins.json`) persisted to a
configurable S3-compatible object store (MinIO in dev and prod), keyed
by `{projectId}/{runId}/relativePath`, surfaced to the host through
`IRunArtifactStore` / `ArtifactPointer` / `IRunArtifactJournalSource` /
`RunTerminalSnapshot`, and read back via
`GET /api/v1/projects/{projectId}/runs/{runId}/artifacts`. The packager
is a host-composed `BackgroundService` that polls the orchestration
schema for terminal-but-not-bundled runs every 10 seconds and emits a
`run.artifacts_bundled` journal event after each successful bundle.

## Requirements

### Requirement: IRunArtifactStore contract
The shared contract `IRunArtifactStore` (in `Comuki.Shared.Contracts.Artifacts`)
SHALL expose `UploadAsync(projectId, runId, relativePath, stream, contentType)`
and `ListAsync(projectId, runId)` operations. `UploadAsync` returns the
canonical URI of the written object; `ListAsync` returns the
`ArtifactPointer` records for every object under the `{projectId}/{runId}/`
prefix. The store knows nothing about the orchestration schema — it is
a pure S3 / MinIO surface scoped by the project + run id tuple.

#### Scenario: Upload under the run prefix
- **WHEN** the packager calls `UploadAsync(projectId, runId, "brief.json", stream, "application/json")`
- **THEN** the object lands at the bucket key
  `{projectId}/{runId}/brief.json` and the call returns its canonical URI

#### Scenario: List returns the run's bundle
- **WHEN** the packager calls `ListAsync(projectId, runId)` for a run
  with two uploaded objects
- **THEN** the call returns two `ArtifactPointer` records (`Name`,
  `Uri`, `Size`, `ContentType`) — the same list the
  `run.artifacts_bundled` event carries

### Requirement: Run artifact packager driver
The artifacts module SHALL ship a `RunArtifactPackager` (per-run bundler)
and a `RunArtifactPackagerService` (`BackgroundService` driver). The
driver SHALL poll every 10 seconds for runs in a terminal status that
have not yet been bundled, run `BundleAsync` for each, and report
`BundleOutcome` results. Per-run exceptions SHALL be logged and skipped;
the loop SHALL continue so a transient MinIO outage does not stall the
queue.

#### Scenario: Terminal run gets bundled on the next poll
- **WHEN** a run has status `Succeeded` (or any terminal status) and has
  no `run_bundles` row
- **THEN** the next driver tick uploads the bundle objects and records
  the `run_bundles` row

#### Scenario: Already-bundled run is skipped
- **WHEN** a run has a `run_bundles` row
- **THEN** the packager logs at debug and skips it; no objects are
  re-uploaded and no new row is written

### Requirement: Host driver wraps the module and emits the journal event
The host SHALL ship a `RunArtifactPackagerHostService` that runs the
artifacts module's packager service and, after each successful
`BundleAsync`, appends a `run.artifacts_bundled` event to the
`run_events` journal. The host driver lives in `Comuki.Host` (not the
artifacts module) because it must reach the engine's
`OrchestrationDbContext` to write the journal row; the artifacts module
deliberately has no engine reference.

#### Scenario: Host driver emits the bundled event
- **WHEN** the host driver observes a non-null `BundleOutcome`
- **THEN** it appends a `run.artifacts_bundled` journal row with the
  outcome's pointer list and a `"bundled"` status label

### Requirement: Bundle objects are brief / result / pins
The packager SHALL upload three objects per bundled run when their source
data is available: `brief.json` (the work item's raw-JSON `Brief`),
`result.json` (the terminal `work_item.status_changed` detail JSON),
and `pins.json` (a compact `{ occurredAt, status }` projection). When
the work item has no `Brief` or the terminal row has no `DetailJson`,
the corresponding object is omitted; `ObjectCount` reflects the
actually-uploaded count.

#### Scenario: All three objects uploaded
- **WHEN** the run has both a `Brief` and a terminal `DetailJson`
- **THEN** the bundle contains `brief.json`, `result.json`, and
  `pins.json` (`ObjectCount = 3`)

#### Scenario: Brief-only bundle
- **WHEN** the terminal row has no `DetailJson`
- **THEN** the bundle contains `brief.json` and `pins.json` only
  (`ObjectCount = 2`); `result.json` is omitted

### Requirement: Artifacts configuration section
The host SHALL bind the `Artifacts` configuration section
(`Artifacts:Endpoint`, `Artifacts:AccessKey`, `Artifacts:SecretKey`,
`Artifacts:Bucket`, `Artifacts:UseSSL`, `Artifacts:AutoCreateBucket`).
`Endpoint`, `AccessKey`, `SecretKey`, and `Bucket` are required and
SHOULD fail the bootstrap loudly when missing. `UseSSL` defaults to
`true`; `AutoCreateBucket` (boolean, default off) creates the bucket
on first use when no other provisioning is in place — dev convenience.
Production deployments rely on the compose `minio-init` job (which
also wires a 30-day non-current-version lifecycle) rather than
`AutoCreateBucket`.

#### Scenario: Dev host auto-creates the bucket
- **WHEN** the bucket does not exist and `Artifacts:AutoCreateBucket=true`
- **THEN** the host creates it on first use and the packager proceeds

#### Scenario: Production leaves provisioning to the operator
- **WHEN** `Artifacts:AutoCreateBucket=false` (the default)
- **THEN** the host does not create the bucket; the deployer is
  responsible (the `minio-init` compose job does it idempotently)

### Requirement: Artifacts module persistence layout
The artifacts module SHALL keep its tables in the `artifacts` Postgres
schema (per-context `<Module>Database.Schema` static class with
`Schema` + table constants — see identity for the pattern). The
`run_bundles` table stores the bookkeeping row the packager writes
after a successful upload; the schema's own `__ef_migrations_history`
table is the migration log. Migrations are applied by the Migrator over
the same connection string (with its own `EnsureSchema` step).

#### Scenario: Packager writes a run_bundles row
- **WHEN** a run is bundled
- **THEN** the artifacts schema carries one `run_bundles` row per
  successful bundle; the row records the `run_id`, `project_id`,
  `status`, `uploaded_at`, and `object_count`

#### Scenario: Migrator ensures the artifacts schema
- **WHEN** the Migrator runs against a fresh database
- **THEN** `EnsureSchema("artifacts")` is called before applying the
  artifacts context's migrations; the schema is created idempotently

### Requirement: v1 retention is "never delete"
v1 SHALL NOT auto-delete run bundles — `v1.0` keeps every successful
bundle indefinitely. Operators MAY add a lifecycle policy (the
`minio-init` job wires a 30-day non-current-version rule) and the
artifacts module SHALL preserve compatibility with such policies. A
`v1.x` retention feature (configurable TTL, lifecycle sweeps) is
explicitly out of scope for this spec and tracked as a follow-up.

#### Scenario: v1 keeps every bundle
- **WHEN** a run is bundled
- **THEN** the bundle objects stay in the bucket indefinitely; the
  artifacts module never schedules a delete

## Boundaries

The artifacts module deliberately has no engine reference. The
`RunTerminalSnapshot` is the shared shape the engine's `IRunArtifactJournalSource`
implementation (host-composed) hands to the packager; the artifacts
module never touches `OrchestrationDbContext`. Per the architecture
rules, the artifacts module depends on `Comuki.Shared.Contracts` only;
the host composes the engine-backed journal source.
