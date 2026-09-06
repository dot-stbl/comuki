## ADDED Requirements

### Requirement: Schedule journal event types

The append-only `run_events` journal SHALL recognize these additional
platform-owned types (the type set remains open):

- `schedule.fired` — payload `{ scheduleId, scheduleSlug, fireKind:
  "cron" | "manual" }`
- `schedule.skipped` — payload `{ scheduleId, reason: "overlap" |
  "secret_unset" | "launch_failed" | "disabled" }`
- `schedule.verdict` — payload `{ scheduleId, from, to }` where from/to
  are `ok` | `fail` | `error` | null

`schedule.fired` and `schedule.verdict` SHALL be appended on the
attributed run. `schedule.skipped` has no run; it SHALL still be
durable (a firing row with `run_id` null plus a structured log). A
skip SHALL NOT invent a run id.

#### Scenario: Successful tick journals fired then verdict

- **WHEN** a cron tick launches a run that succeeds
- **THEN** the run journal contains `schedule.fired` then later
  `schedule.verdict` with `to = ok`

#### Scenario: Overlap skip has no run

- **WHEN** a due job is skipped for overlap
- **THEN** a firing row is stored with a skip reason and no run id
