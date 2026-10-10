## Why

Today Comuki fuses the user goal, the admission envelope, and the technical
execution into one entity: an admitted ticket creates a `Run`, and a `Run` is
both the durable thing a user cares about and the place workers do work. That
shortcut breaks the moment the same work must survive revision, replacement,
retries, blockers, or "still relevant after the original tracker ticket was
closed." Standalone Tasks sit between admission and execution: a Task is the
durable user-facing unit of work, a Run becomes one execution *attempt* of
that Task, and everything downstream (tracker sync, brain assignment,
completions, retry/waiver Decisions) reasons in terms of the Task.

## What Changes

- Introduce the Work bounded context: a `WorkTask` aggregate with its own
  deterministic status machine, versioned brief, source links, dependency
  graph, sequential Run-attempt ledger, and resolved-outcome enum.
- Connect Work to Orchestration through idempotent outbox/inbox commands and
  terminal events so a Task has at most one active Run under concurrent
  dispatch/replacement.
- Replace the admission-side "create Run" shortcut with "create or resolve
  one standalone Task before first Run", while keeping compatibility
  projections so existing Run-shaped API/dashboard/CLI consumers keep
  working while Task becomes authoritative.
- Move tracker synchronization to Task resolution with dedupe so a failed
  first attempt followed by a successful replacement publishes one final
  outcome, never intermediate spam.
- Add explicit `Blocked` handling: exhausted attempts and explicit
  retry/replacement/waiver/failed-resolution/cancellation Decisions drive
  the Task forward; no LLM output may bypass the deterministic machine.
- Backfill Claimed/active legacy Runs into attempt-1 Tasks and reconcile
  unsynced terminal Runs per the umbrella's existing-state cutover matrix.
- Extend Work coordination: multi-source refs with Decision-controlled
  primary change, responsible human/service actors as attention metadata
  without authorization effects, cross-Mission blocking edges with redacted
  dependency stubs, and predeclared Task completion policies with
  reviewer separation.

## Capabilities

### New Capabilities

- `work-management`: standalone Tasks between intake and execution, with
  their lifecycle, resolution outcomes, source links, dependency graph,
  sequential Run attempts, completion policies, and the Work execution
  process that owns dispatch, verification, and resolution.

### Modified Capabilities

None — `work-management` is purely additive. The Intake→Integrations hard
rename and the runs/worker-runtime orchestration changes are owned by sibling
changes `add-execution-spine-orchestration` (#87) and
`add-hard-rename-intake-to-integrations` (#88); `work-management` only
consumes the post-rename target shape.

## Impact

- New module project tree: `platform/src/modules/Work/` with the standard
  `Comuki.Modules.Work.{Domain,Application,Infrastructure}` three-project
  shape (architecture.md target layout), plus a new `__comuki_work`
  PostgreSQL schema, WorkDbContext, and dedicated `outbox_messages` /
  `inbox_receipts` configured by `Comuki.Shared.Messaging`.
- New host API surface: `platform/src/host/Comuki.Host/Api/Work/` with
  WorkTask read/write endpoints, plus composite read projections under
  `HostComposer` for clients that combine Task + Run + attempts.
- Capability Broker surface for `work.*` commands and queries
  (architecture.md "### Work" interface block).
- Migration: one new migrator step `Comuki.Migrator` plus an additive
  backfill job that walks the existing Run / sync_job / IncomingTicket
  tables per the cutover matrix in
  `add-mission-cowork/design.md` §Migration Plan.
- Depends on #87 (`add-execution-spine-orchestration`) for the durable
  per-context outbox/inbox and execution generation fencing, and on #88
  (`add-hard-rename-intake-to-integrations`) for the `InboundItem` /
  `integration.inbound.admitted` admission shape this change consumes.

## Non-goals

- Mission membership, collaboration, room stream, proposals, completion
  review (owned by `add-minimal-missions` / `add-mission-stream-and-room` /
  `add-mission-completion-review`).
- Worker leases, slot isolation, execution generation fencing
  implementation, Host-composed claim/heartbeat plumbing (owned by
  `add-execution-spine-orchestration` #87 and
  `add-worker-pools-and-isolation-classes` #100). Work invokes Orchestration
  through the same idempotent command seam every other context uses; it does
  not own the queue mechanics (see `work-queue`).
- The Intake → Integrations rename itself (owned by #88); this change
  consumes the renamed `InboundItem` shape and `integration.inbound.admitted`
  event as already-landed preconditions, not as code it edits.
- Capability Broker wiring for the `work.*` surface end to end. The
  commands' existence and contracts are specified here; their migration
  behind the Broker is owned by `add-capability-broker` task 5.7.
- Mission/Task creation, activation, cancellation, completion, deletion,
  orphan recovery, and goal-revision impact maps (task 19.4, owned by
  `add-minimal-missions` #93).