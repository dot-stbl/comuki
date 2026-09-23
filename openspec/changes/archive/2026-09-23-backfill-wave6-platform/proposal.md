## Why

Wave 6 / S8–S9 platform deltas shipped without OpenSpec updates: Kubernetes
compute provider, OTel + Grafana as-code, subject-scope EF filters, runs list
+ filter DSL.

## What Changes

- Extend `compute` with Kubernetes batch/v1 Job provider contract.
- Extend `identity` / orchestration visibility with subject-scope ambient
  filters (404-not-deny object axis).
- Extend `runs` with list API + filter/sort DSL (and add shared `filtering`
  capability for the grammar).
- Extend `host` with telemetry installer opt-in and Grafana dashboards note.

## Capabilities

### New Capabilities
- `filtering`: shared list filter/sort DSL used by runs (and future lists)

### Modified Capabilities
- `compute`: Kubernetes provider beside Docker
- `identity`: ambient subject scope for the object axis (with host middleware)
- `runs`: `GET /api/v1/runs` list + filter + scope filtering
- `host`: OTel wiring when `Telemetry:OtlpEndpoint` is set

## Impact

Docs only. Code in Engine.Compute/Kubernetes, Shared.Filtering,
Shared.Telemetry, OrchestrationDbContext filters, Host/Runs, deploy/grafana.

## Non-goals

- kind e2e cluster tests as CI gate
- Expanding filterable fields beyond Status/CreatedAt/UpdatedAt on runs
