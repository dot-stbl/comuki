# Verification Specification

## Purpose

Defines the orthogonal verification axis for a Run: the `VerificationRecord` per WorkItem, the gate-provider registry (SPI), the evidence pointer list, the platform-shipped first provider (the existing `Verify` module, master `2d7346dc`, `Verify:Verifier:Enabled=false`), the `ProjectSettings.VerifyEnabled` wire, and the derived view that exposes verification status without changing the seven-state run transition table.

Done is not verified. Today the run state machine accepts every `Succeeded` claim at face value; a worker that finished with incomplete work is indistinguishable from a worker that finished with the verified proof. This capability makes the axis orthogonal: the run state machine stays seven states; `VerificationRecord` is a sibling.

## ADDED Requirements

### Requirement: VerificationRecord is a per-WorkItem sibling table

The platform SHALL store `VerificationRecord` rows in a sibling table `verifications` (snake_case columns), indexed on `(work_item_id, gate_name)`. The schema carries `WorkItemId`, `GateName`, `Verdict` (`pending` | `passed` | `failed`), `EvidenceRefs` (jsonb array of `{ Kind: cmdiff|stdout|stderr|json, Uri }`), `EvaluatedAt`, `Evaluator`. The migration is `dotnet ef migrations add AddVerificationRecord` — never hand-edited.

#### Scenario: First gate verdict lands

- **WHEN** a registered gate provider evaluates a WorkItem and returns `passed`
- **THEN** a `VerificationRecord` row is committed in the same transaction as the gate's `gate_evaluated` journal event

#### Scenario: Per-gate uniqueness

- **WHEN** the same gate evaluates the same WorkItem twice
- **THEN** the second evaluation produces an updated row (upsert on `(work_item_id, gate_name)`), not a duplicate row

### Requirement: Gate-provider registry is an open SPI

`IVerificationGateProvider` is the SPI:

```csharp
public interface IVerificationGateProvider
{
    string GateName { get; }
    bool AppliesTo(WorkItem item, ProjectSettings settings);
    Task<GateVerdict> EvaluateAsync(WorkItem item, RunContext context, CancellationToken ct);
}
```

The registry is a typed `AddVerificationGateProvider<T>(string gateName)` host extension. Providers are constructed through DI; the platform iterates registered providers per WorkItem and collects verdicts; the iteration order is registration order.

#### Scenario: First registered provider is the existing Verify module

- **WHEN** the host composes the verification module
- **THEN** the existing `Verify` module (master, `GenericCommandRun`) is registered with `gateName = "verify:generic-command-run"`, `Verify:Verifier:Enabled` flips to `true`, and `GenericCommandRun`'s `Pending → Running → Green/Red` flow stamps the result as a `gate_evaluated` event

#### Scenario: Author authors a second provider

- **WHEN** an operator authors a class `MyCustomProvider : IVerificationGateProvider` and registers it through `AddVerificationGateProvider<MyCustomProvider>("custom:my-team-check")`
- **THEN** the new provider is iterated alongside the platform-shipped one, no platform code changes

### Requirement: gate_evaluated journal event

The `run_events` journal SHALL record a `gate_evaluated` event in the same transaction as the `VerificationRecord` upsert. The payload carries `{ gate_name, verdict, evidence_refs: string[] }` where `evidence_refs[i]` is the canonical URI of a member in the run's MinIO bundle. The event type is open — gate providers may emit their own dotted kinds (the type set stays open per the existing `run_events` contract).

#### Scenario: gate_evaluated on the timeline

- **WHEN** a gate provider evaluates a WorkItem
- **THEN** the run journal contains a `gate_evaluated` entry whose payload names the gate, verdict, and evidence URIs

#### Scenario: Provider-specific event kind

- **WHEN** a gate provider also emits a provider-specific event kind (e.g. `verify.generic-command-run.exited`)
- **THEN** the journal accepts the additional event under the existing open type-set rule; no schema migration is required

### Requirement: ProjectSettings.VerifyEnabled wires the verification path

`ProjectSettings.VerifyEnabled` (existing field, 0 consumers) SHALL gate the verification path:

- `false` (default): gates are skipped, `VerificationRecord` rows are not written, the run lands as `Succeeded` with the existing semantics.
- `true`: gates are required; a `Succeeded` run with no `passed` gate verdicts gets the visible "verification pending" annotation through the derived view, not a new run state.

The field exists; this capability wires its first consumer. The wiring does not change the seven run states or the transition table.

#### Scenario: VerifyEnabled = false keeps the existing path

- **WHEN** a project has `VerifyEnabled = false`
- **THEN** gates are skipped and `VerificationRecord` rows are not written

#### Scenario: VerifyEnabled = true with zero passed gates

- **WHEN** a project has `VerifyEnabled = true` and a run's WorkItems have only `pending` and `failed` gate verdicts
- **THEN** the run lands as `Succeeded` (the existing semantics) and the derived view returns the `verification_pending` annotation

#### Scenario: VerifyEnabled = true with all passed gates

- **WHEN** a project has `VerifyEnabled = true` and every required WorkItem has at least one `passed` gate verdict
- **THEN** the run lands as `Succeeded` (existing semantics) and the derived view returns `verified: true`

### Requirement: Bundle accepts text/x-diff evidence

The worker artifact upload endpoint (`POST /workers/{workItemId}/artifacts`) SHALL extend its mime allow-list with `text/x-diff`. A worker uploading a `changeset.diff` lands the bundle member under the existing `{project}/{run}/` prefix. The named key `cmdiff` in the `artifacts` table points to the bundle member.

#### Scenario: Worker uploads a diff

- **WHEN** a worker posts `text/x-diff` content to the artifact upload endpoint with name `changeset.diff`
- **THEN** the bundle member lands in `{project}/{run}/changeset.diff` and the `artifacts.cmdiff` row points to it

#### Scenario: Disallowed mime refused

- **WHEN** a worker posts an unsupported mime (e.g. `application/x-msdownload`)
- **THEN** the upload answers `415 Unsupported Media Type` and nothing is stored

### Requirement: Verification view is a derived read

`GET /api/v1/runs/{runId}/verification` SHALL return:

```json
{
  "runId": "...",
  "verified": true | false,
  "verificationPending": true | false,
  "gates": [
    {
      "gateName": "verify:generic-command-run",
      "workItemId": "...",
      "verdict": "pending|passed|failed",
      "evidence": [{ "kind": "cmdiff", "uri": "..." }, ...],
      "evaluatedAt": "..."
    }
  ]
}
```

The endpoint requires `run:read` (no new permission). The view is a *derived* query over `verifications` ∪ `run_events` ∪ `artifacts`; no write path.

#### Scenario: View with all-passed gates

- **WHEN** a `Succeeded` run has every required WorkItem with `passed` gate verdicts
- **THEN** the view returns `verified: true`, `verificationPending: false`, and the per-gate list

#### Scenario: View with verification pending

- **WHEN** a `Succeeded` run has zero `passed` gate verdicts and `VerifyEnabled = true`
- **THEN** the view returns `verified: false`, `verificationPending: true`, and the per-gate list with `verdict: pending` entries

## ADAPTER Notes

The seven run states, the `run_events` open type-set, and the `run_events` append-only contract are unchanged. `VerificationRecord` is a sibling table — not a column on `work_items`, not a run state. The `Verify` module (master) is the first registered provider; `Verify:Verifier:Enabled` flips from `false` to `true` only when this capability ships. The capability is additive; rollback is "flip the flag back and drop the gate_evaluated journal filter".
