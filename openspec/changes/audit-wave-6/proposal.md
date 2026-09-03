## Why

The previous OpenSpec backfill (`backfill-wave6-platform`) covered
Wave 6 platform seams (K8s compute provider, OTel + Grafana, runs
list + filter DSL, subject-scope ambient filters). Since that change
landed, twelve issues closed (#18, #19, #20, #21, #22, #23, #24, #25,
#26, #27, #28, #29 — the brief misnumbered the first two as #17 / #18)
that did NOT receive an OpenSpec delta: inbound PR-review (PR/MR
discriminator + `IIntakeProfileRouter` + `pr-review` profile),
run-artifact bundle (a brand-new `Artifacts` module with MinIO,
packager driver, journal event, read API), SignalR detailed-errors
gating, `TypedResults.Problem` convention across all controllers,
Migrator password env var + Production gate, the `ApiRoutes` literal
fix, the `*Dto` suffix drop, the folder-cap cleanup, the per-schema
Postgres schemas, and the OpenAPI document emission + kubb alignment.

The audit change records the spec surface for those seams (a brand-new
`artifacts` capability, delta specs for `runs` / `intake` / `host` /
`realtime`) and points out gaps in the brief's premise so the
remediation work is targeted rather than guess-driven.

## What Changes

- New capability `artifacts`: `IRunArtifactStore` / `ArtifactPointer` /
  `RunTerminalSnapshot` contracts; per-run packager + host driver;
  `run_bundled` journal event; `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts`;
  `Artifacts:*` config section; v1 retention = never delete.
- Extend `runs` with the `run.artifacts_bundled` journal event,
  `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts` read API,
  and the seven `DeliveryOutcomes` labels that the intake webhook
  pipeline records.
- Extend `intake` with the `InboundTicketKind` discriminator
  (`Issue` / `PullRequest`) and the `IIntakeProfileRouter` port.
- Extend `host` with the SignalR detailed-errors gates, the
  `TypedResults.Problem` convention, the Migrator password env var +
  Production gate, and the `ProjectCosts` / `RunArtifacts` route
  constant split.
- Extend `realtime` with the `run.artifacts_bundled` event broadcast
  via the existing `IRunEventsBroadcaster`.

## Capabilities

### New Capabilities
- `artifacts`: run-artifact bundle (MinIO / S3 store, packager driver,
  read API, journal event)

### Modified Capabilities
- `runs`: `run.artifacts_bundled` event + read API + delivery outcome
  labels
- `intake`: `InboundTicketKind` discriminator + `IIntakeProfileRouter`
  port
- `host`: SignalR gates + `TypedResults.Problem` convention + Migrator
  password + route constants
- `realtime`: bundle event broadcast

## Impact

Docs only. No runtime changes. The tasks file is the actionable
remediation; closing each task is a single edit to the existing
spec files (or, for the brand-new `artifacts` capability, a fresh
spec.md).

## Non-goals

- Resyncing the brief's misnumbering of issues #17 / #18 (the audit
  document flags the discrepancy; the canonical numbering is from `gh
  issue list`).
- Adding a `v1.x` retention feature for run bundles (the `artifacts`
  spec explicitly documents v1 as "never delete").
- Adding a `requires_recently_bundled` permission or per-run artifact
  ACLs (the `RunArtifactsController` shares `run:read` with the parent
  RunsController).
- Cataloging the Wave 6 changes that are already in
  `backfill-wave6-platform`; this audit picks up after that change.
