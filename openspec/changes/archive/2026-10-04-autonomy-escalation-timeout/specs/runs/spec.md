## ADDED Requirements

### Requirement: Escalation timeout journal event type

The append-only `run_events` journal SHALL recognize an additional
platform-owned type (the type set remains open):

- `run.escalation_timeout` — payload `{ runId, from: "Escalated", to:
  "Cancelled", ageSeconds }`, appended by the escalation-timeout sweeper
  in the same transaction as the status change it records

This event is emitted instead of `run.status_changed` for
sweeper-driven transitions, mirroring how `work_item.lease_expired` is
its own event family rather than a `work_item.status_changed` entry —
the distinct type makes it unambiguous in the timeline which terminal
transitions were system-driven versus human-driven.

#### Scenario: Auto-archive is distinguishable from a human decision
- **WHEN** the escalation-timeout sweeper transitions a run from
  `Escalated` to `Cancelled`
- **THEN** the journal carries a `run.escalation_timeout` entry (not a
  `run.status_changed` entry) whose payload names `from`, `to`, and the
  number of seconds the run sat in `Escalated`

### Requirement: Escalation timeout sweep policy

A background sweeper SHALL passively demote runs stuck in `Escalated`
with no human action: every `SweepInterval`, it transitions every run
whose status is still `Escalated` and whose `updated_at` is older than
`now - EscalationTimeout` to `Cancelled`, guarded by a single
`UPDATE ... WHERE status = 'Escalated' AND updated_at < @cutoff`
statement so a concurrent human re-queue (which changes the status
first) always wins the race. Each archived run SHALL be journalled with
one `run.escalation_timeout` event in the same transaction as the
status change. The sweep is one-directional — it only ever moves
`Escalated` → `Cancelled`, never the reverse, and never touches any
other status.

Configuration is bound from `Orchestration:EscalationTimeout`:

| Setting | Type | Range | Default | Notes |
|---|---|---|---|---|
| `EscalationTimeout` | TimeSpan | `00:05:00`–`1.00:00:00` | `01:00:00` | How long a run may sit in `Escalated` before it is auto-archived. |
| `SweepInterval` | TimeSpan | `00:00:05`–`00:05:00` | `00:00:15` | Cycle period. |
| `Enabled` | bool | — | `true` | Ops kill-switch; the worker still registers but returns immediately when `false`. |

There is no per-project override and no permission gate in v1.x — the
sweeper is invisible to operators beyond the config knob.

#### Scenario: Stale escalated run is auto-archived
- **WHEN** a run has sat in `Escalated` with `updated_at` older than
  `now - Orchestration:EscalationTimeout:EscalationTimeout`
- **THEN** the sweep transitions it to `Cancelled` and appends a
  `run.escalation_timeout` journal entry carrying the age in seconds

#### Scenario: Human re-queue wins the race
- **WHEN** an operator transitions a run out of `Escalated` (for example
  via the approval endpoint) concurrently with a sweep pass
- **THEN** the sweeper's guarded `WHERE status = 'Escalated'` predicate
  no longer matches that row, so the row keeps the human's transition
  and is not also archived

#### Scenario: Disabled flag short-circuits the sweep
- **WHEN** `Orchestration:EscalationTimeout:Enabled = false`
- **THEN** the escalation-timeout worker returns immediately without
  running a sweep pass
