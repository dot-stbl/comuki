## MODIFIED Requirements

### Requirement: Wall-clock deadlines do not change the run state machine

The run state machine SHALL remain **seven states** (`Pending`, `Claimed`, `Running`, `Succeeded`, `Failed`, `Cancelled`, `Retrying`) per `openspec/specs/runs/spec.md`. Wall-clock deadlines (`TranslatorOptions.TurnBudget` per cycle, `RunBudget` per worker process) SHALL NOT add new states; they are enforced by the Translator's `DeadlinePolicy` and the `WorkerProgressWatchdog` (per `worker-runtime` capability delta). A `TurnBudget` breach SHALL cause a `gentle_kill` (tier 2); a `RunBudget` breach after `red_pattern = 3` consecutive turn-budget breaches SHALL fail the item with reason `worker.stall_detected` and the run SHALL transition to `Failed` through the existing path. `worker.exit_code` journal events (`exit_no_stage_report` parse-error counter) SHALL be **visibility**, not authority — the run state machine SHALL NOT transition on exit-code alone.

#### Scenario: TurnBudget breach causes gentle-kill

- **WHEN** a worker's harness parses no events for `TranslatorOptions.TurnBudget`
- **THEN** the `DeadlinePolicy` cancels the cycle (tier 2 gentle-kill); the run transitions through the existing cancel path; the `Run` ends in `Cancelled` (not a new state)

#### Scenario: RunBudget breach after three consecutive TurnBreach fails the item

- **WHEN** a worker has reported three consecutive `TurnBudget` breaches (or any consecutive tier-2 escalations)
- **THEN** the next `RunBudget` escalation fails the item with reason `worker.stall_detected`; the run transitions to `Failed` through the existing path

#### Scenario: Run state machine is closed

- **WHEN** the deadlines fire
- **THEN** no new run state appears in `run_events.journal`; the run transitions through the existing seven-state machine

### Requirement: TrustClass promotion driven by verification evidence

The `RunTrustClass` aggregate (on master: `Comuki.Engine.Orchestration.Domain.RunTrustClass`) SHALL be promoted or demoted by a `TrustClassPromotionPolicy` consumer that observes `VerificationRecord` rows (`gate_evaluated` events per `add-orchestra` §3.1). The policy:

- **Promotion**: `K_green` consecutive `gate_evaluated:passed` events within a `window_ms` sliding window → `RunTrustClass.SetAsync(runId, promotion_target)`. Default `K_green = 3`, `window_ms = 24h`, `promotion_target = RunTrustClass.Max`.
- **Demotion**: `red_pattern` consecutive `gate_evaluated:failed` events → `RunTrustClass.SetAsync(runId, predecessor)`. Default `red_pattern = 3`, `predecessor = RunTrustClass.Standard`.
- The policy SHALL be configurable via `OrchestrationOptions.TrustClassPromotionPolicy` with `[Range]` validation and `ValidateOnStart()`.
- Promotion/demotion SHALL be **per-RunId**; no cross-RunId contamination.
- The `TrustClassPromotionConsumer` (`Comuki.Engine.Orchestration.Application.TrustClassPromotionConsumer`) SHALL read `IVerificationRecordStore.ListRecentByProjectAsync(projectId, window_ms)`; the `VerificationEvaluationService` (added in `add-orchestra` §3.2 commit `b9fe26dc`) SHALL be the publisher.

> **Coordination note.** `autonomy-escalation-timeout` (passive
> down-ratchet on activity timeout) is **separate** from
> TrustClass promotion (active up/down on evidence). Both channels
> exist; they do not replace each other. TrustClass promotion does
> not retire `autonomy-escalation-timeout`.

#### Scenario: Promotion after three consecutive passed gates

- **WHEN** `TrustClassPromotionPolicy{K_green = 3, window_ms = 24h}` observes three consecutive `gate_evaluated:passed` events for `runId = R1` within the window
- **THEN** `RunTrustClass.SetAsync(R1, RunTrustClass.Max)` is invoked exactly once

#### Scenario: Demotion after three consecutive failed gates

- **WHEN** the policy observes three consecutive `gate_evaluated:failed` events for `runId = R1`
- **THEN** `RunTrustClass.SetAsync(R1, RunTrustClass.Standard)` is invoked exactly once

#### Scenario: Mixed verdicts do not promote

- **WHEN** the policy observes a sequence of mixed `gate_evaluated` events (passed, failed, passed) for `runId = R1`
- **THEN** no `RunTrustClass.SetAsync` call is invoked; the consumer does not promote or demote on mixed evidence

#### Scenario: Per-RunId isolation

- **WHEN** the consumer observes `gate_evaluated` events for two distinct `RunId`s (`R1` and `R2`) within the same project
- **THEN** promotion for `R1` does not affect `R2`'s `RunTrustClass`; each RunId's promotion state is independent

### Requirement: Worker wall-clock budgets are wall-clock on the worker process, not on task completion

The worker's wall-clock budgets (`TurnBudget`, `RunBudget`) are measured in **wall-clock elapsed time** for the worker process and cycle respectively. They are **not** the same as the brain-ops `TurnBudget` (`add-mission-cowork` task-completion semantics, default 5/15/30 minutes by project profile). The two budgets are independent axes:

- **Brain-ops TurnBudget** — wall-clock on a Brain task completion attempt; "5 m on a single attempt"; breach → Brain task retries with new attempt.
- **Worker TurnBudget** — wall-clock on a single worker cycle (claim → spawn → StageReport); breach → tier 2 gentle-kill (the worker process is cancelled; lease is lost; the run reaper handles the next claim).

A profile that overrides either budget (`OrchestrationOptions.BrainTaskToolTimeout`, `TranslatorOptions.TurnBudget`) MUST NOT implicitly affect the other.

#### Scenario: Brain-ops budget does not affect worker TurnBudget

- **WHEN** a project's brain-ops TurnBudget is set to 5 minutes
- **THEN** the worker TurnBudget remains at its default (60 minutes) unless explicitly overridden

#### Scenario: Worker TurnBudget does not affect brain-ops budget

- **WHEN** a worker's TurnBudget is overridden to 5 minutes via `control-plane/profiles/<name>.md` `turn_budget_min` frontmatter
- **THEN** the brain-ops TurnBudget remains at the project's configured value

## ADAPTER Notes

The seven-state run machine (`Pending` / `Claimed` / `Running` / `Succeeded` / `Failed` / `Cancelled` / `Retrying`) is unchanged. Wall-clock deadlines are *enforcement* on the worker side, not a state-machine widening. The `VerificationRecord` journal (added in `add-orchestra` §3.1) is the input to the `TrustClassPromotionConsumer`; the consumer is the first real subscriber to the journal for up/down promotion (the verification-pending annotation, the per-gate evidence view, and other consumers added earlier — see `add-orchestra` §3.5/§3.6 — are unchanged).

The `RunTrustClass` smoke types are unchanged (`Trusted | Standard | Limited | Isolated`). The promotion policy decides transitions between them; the domain does not. The policy's `K_green`, `window_ms`, `red_pattern`, `promotion_target`, `predecessor` are configuration knobs on `OrchestrationOptions`, not domain fields.