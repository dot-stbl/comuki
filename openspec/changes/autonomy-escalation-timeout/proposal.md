## Why

Issue #11 lists **Autonomy ratchet (классы доверия)** as a post-v1 backlog
item. This change ships the first sub-slice — a passive ratchet on the
**Escalated** terminal state: when a run sits in `Escalated` longer than
a configurable timeout with no human action, the system auto-archives it
(transition to `Cancelled`) and journals a `run.escalation_timeout`
audit row. The ratchet is one-sided: it never *escalates*, only
*demotes*. Higher autonomy classes (auto-pass decisions, per-decision
confidence, supervisor polling) come later as separate OpenSpec changes.

This is the smallest cohesive piece of the autonomy backlog because it
reuses the existing `RunStatus.Escalated` value and `Escalated → Cancelled`
transition that is already in `RunTransitions`. No new entity, no new
migration, no new port.

## What Changes

- New `EscalationTimeoutSweeper` in `Comuki.Engine.Orchestration`
  (Infrastructure): one `Run` query per pass that finds `Escalated`
  rows older than `EscalationTimeoutOptions.EscalationTimeout`,
  transitions them to `Cancelled`, and journals one
  `run.escalation_timeout` event per row in a single transaction.
- New `EscalationTimeoutWorker : BackgroundService` that ticks at
  `EscalationTimeoutOptions.SweepInterval`, opens a fresh DI scope,
  enters `AsSystem("escalation-timeout-sweeper")`, resolves the sweeper
  and runs one pass.
- New `EscalationTimeoutOptions` (config section
  `Orchestration:EscalationTimeout`): `EscalationTimeout` (default 1h,
  range 5m–24h), `SweepInterval` (default 15s, range 5s–5m). Both
  `ValidateOnStart`.
- New `RunEventTypes.RunEscalationTimeout = "run.escalation_timeout"`
  constant + typed payload helper.
- Wire options + sweeper + worker in
  `OrchestrationInfrastructureExtensions.AddOrchestrationQueue`.

## Capabilities

### New Capabilities

- `escalation-timeout`: passive autonomy ratchet on the Escalated state.

### Modified Capabilities

- `runs`: new journal event type `run.escalation_timeout`.

## Impact

- New code in `platform/src/engine/Comuki.Engine.Orchestration/`:
  - `Options/EscalationTimeoutOptions.cs`
  - `Infrastructure/EscalationTimeout/EscalationTimeoutSweeper.cs`
  - `Infrastructure/Hosting/EscalationTimeoutWorker.cs`
  - `Infrastructure/Journal/RunStatusChangePayloads.cs`
- Edit `Domain/Journal/RunEventTypes.cs` (one new constant).
- Edit `Infrastructure/OrchestrationInfrastructureExtensions.cs`
  (one options bind + one scoped add + one hosted service add).
- New integration test in
  `tests/integration/Comuki.Host.Integration.Runs/EscalationTimeoutSweeperShould.cs`.
- No migration. No new entity. No new permission.

## Non-goals

- Auto-approving escalated runs from a human-less review window
  (autonomy *upgrade* ratchet — separate slice, post-v1).
- Per-decision confidence thresholds (separate slice, needs Intake store).
- Notification fan-out when the ratchet fires (journal is enough for v1.x;
  chat/tracker sinks come later).
- Multi-replica fencing beyond the existing system-scope query filter.
  A second Host replica could race the sweep; a `FOR UPDATE SKIP LOCKED`
  raw-SQL variant is a follow-up if Redis-fencing (#11) ships first.
- Hand-edited migration. The sweeper is pure application logic over the
  unchanged `runs` / `run_events` tables; nothing in the EF model moves.