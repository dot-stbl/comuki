## Why

Comuki's orchestration spine has been extended incrementally to carry
Mission/Work automation on top of it, but the layer below remains unsafe
for that load. WorkItem dependency edges exist only as inert storage
(`work_item_dependencies`, `Blocked` entry status) with no enforcement at
claim — the claim query matches on status/profile labels alone. A worker
can authoritatively complete a WorkItem on a stale execution after an
operator cancellation or a superseding attempt, because nothing fences
WorkItem mutations to the Run's current attempt. A committed terminal Run
fact is delivered only through best-effort SignalR
(`SignalRRunEventsBroadcaster`), with no durable outbox — a process
failure between commit and broadcast silently drops the terminal event
that Work/Mission automation will depend on. Intake's admission-to-Run
path (`IntakeRunLauncher`) is a single synchronous call that can orphan a
Run, or double-launch one, if the two writes split across a crash or a
retried delivery. `add-mission-cowork`'s design.md designates this change
as **Gate B**: every subsequent Work/Mission/Broker child change (#88
through #104) is declared to wait behind it.

## What Changes

- Enforce WorkItem dependency readiness at claim: a `Blocked` item
  unblocks only when its `work_item_dependencies` prerequisites reach
  `Succeeded`; the claim query stops matching on label alone.
- Make Run terminal reconciliation exactly-once under concurrency: the
  last terminal WorkItem finalizes its parent Run's status once, proven
  under concurrent completions.
- **Internal breaking change:** remove the `Failed → Queued` Run retry
  edge; a Failed Run is terminal like Succeeded/Cancelled. A user-visible
  retry becomes "create the next Run attempt," not "requeue this Run."
  Bounded WorkItem-level lease retries (the reaper's `Running → Queued`
  requeue) are unaffected.
- Introduce execution generation fencing on the existing WorkerId+lease
  ownership model: cancelling or superseding a Run bumps its generation
  and fences every live WorkItem under it; heartbeat/complete/fail verify
  the current generation in addition to lease ownership, so a late worker
  cannot authoritatively finish after replacement.
- Strengthen the existing Run cancellation endpoint
  (`POST /runs/{id}/cancel`) to fence live executions before a
  replacement Run may activate.
- Add a durable terminal-event outbox (`outbox_messages`/
  `inbox_receipts`, `FOR UPDATE SKIP LOCKED` dispatch) distinct from the
  existing best-effort SignalR broadcast, plus an internal event-contract
  compatibility gate (`orchestration.run.*.v1`, additive-only within a
  major) and a breaking-deployment runbook.
- Close the Intake claim/admission orphan-Run race using the same
  outbox/inbox pattern for idempotent Run creation on claim.
- Prove the whole spine — dependency, terminal, cancellation/fencing,
  outbox, late-result rejection — through one crown scenario extending
  `Comuki.EndToEnd.AgentLoop` and `TranslatorE2EShould`.

## Capabilities

### Modified Capabilities

- `runs`: dependency-gated claim, exactly-once terminal reconciliation,
  immutable terminal Run status, execution generation fencing, durable
  terminal outbox distinct from realtime broadcast, idempotent Run
  creation on admission.
- `worker-runtime`: heartbeat/complete/fail on the existing
  WorkerId+lease ownership model additionally verify an execution
  generation — a narrow addition, no slot/WorkerHostId vocabulary; that
  generalization belongs to `add-worker-pools-and-isolation-classes`
  (#100).

## Impact

`platform/src/engine/Comuki.Engine.Orchestration/**`,
`platform/src/host/Comuki.Host/Runs/**`,
`platform/src/host/Comuki.Host/Workers/Api/**`,
`platform/src/host/Comuki.Host/Intake/IntakeRunLauncher.cs` (call site
only), new outbox/inbox infrastructure. No dashboard/CLI/API-contract
surface changes — this change is entirely below the Task/Mission layer.
Every subsequent Work/Mission/Broker child change (#88–#104) is blocked
behind this one per design.md Gate B and decomposition.md's Wave 1.

## Non-goals

- Introducing `WorkerHostId`/`SlotId`/multi-slot warm hosts — that is
  `add-worker-pools-and-isolation-classes` (#100); this change fences the
  single-worker model that exists today.
- Any Task/WorkTask/Mission concept — `Run` stays "one execution
  attempt," not yet attached to a durable user-facing Task (that
  attachment is `add-work-management`, #89).
- A generic saga/workflow engine — outbox/inbox choreography only, per
  `add-mission-cowork/design.md` decision #3.
- Changing the worker gRPC stream contract, Translator loop, container
  image, or intake outcome-label set (`admitted`/`pending`/`filtered`/
  `skipped`/`duplicate`/`rejected`/`replay` are unchanged).
