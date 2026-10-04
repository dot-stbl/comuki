## ADDED Requirements

### Requirement: Verification view is a derived read (canonical: verification)

> **Coordination note (2026-10-04).** The `GET /api/v1/runs/{runId}/verification` endpoint, the response body shape, the per-gate scenarios, and the "Run terminal phase is augmented, not widened" requirement are all owned by the **`verification` capability** (see `verification/spec.md` §"Verification view is a derived read"). The `runs` capability owns the seven-state run transition table and the `Run artifacts read API is unchanged` requirement; it references the verification view here so a reader of either capability sees the cross-link.

The `runs` capability's contribution to this change:

- The seven run states (`Queued` / `Waiting` / `Running` / `Succeeded` / `Failed` / `Cancelled` / `Escalated`) and the run transition table are unchanged. The verification axis is *orthogonal*: a `Succeeded` run may carry a "verification pending" derivation without a transition. The terminal-reconciliation SQL in `WorkItemQueueSql.cs` is unchanged; the golden fixtures in `tests/unit/Comuki.Engine.Orchestration.Unit.Eval` continue to pass.
- `GET /api/v1/projects/{projectId:guid}/runs/{runId:guid}/artifacts` continues to return the `run.artifacts_bundled` pointer list (the verification view's `evidence` array reuses the same canonical URIs per the existing artifact bundle's `cmdiff` member; no new artifact endpoint is added).

#### Scenario: Terminal reconciliation is unchanged

- **WHEN** the last required WorkItem in a Run reaches `Succeeded`
- **THEN** the Run transitions to `Succeeded` exactly once through the existing reconciliation path; the verification view is a separate read query (see `verification/spec.md`)

#### Scenario: Golden fixtures still pass

- **WHEN** `dotnet run --project tests/unit/Comuki.Engine.Orchestration.Unit.Eval` runs
- **THEN** every transition-table test passes without modification

#### Scenario: artifacts endpoint still returns the bundle

- **WHEN** an authorised caller GETs `/runs/{runId}/artifacts` for a run whose packager has bundled
- **THEN** the response is `200` with the same pointer list the `run.artifacts_bundled` event carried

#### Scenario: cmdiff appears under the canonical key

- **WHEN** a worker has uploaded a `changeset.diff` to the bundle
- **THEN** the `artifacts` table row carries `name = "cmdiff"` and the URI points to the bundle member; the verification view's `evidence` array reads the same row (see `verification/spec.md`)