## Why

**Gated: this wave must not start until `add-scheduled-jobs` lands** — that
change is actively editing the Scheduler module (4/19 tasks at filing
time), and migrating view projections onto a moving target would collide.
The gate is recorded in `adopt-mapperly/design.md` ("blocked on
`add-scheduled-jobs`") and in the backend-mapping spec ("Scheduler
adoption waits for the in-flight scheduled-jobs change to land first").

Once ungated, this is the final sized wave of the Mapperly adoption planned
in `adopt-mapperly` (filed per its task 7.6): the Scheduler module's
`ScheduledJobView.Of`/`OfAll` plus the `ScheduledJobsPageView` page
composition in `ScheduledJobService` (design table: "1 + page") migrate to
the generated-mapper convention.

This is a proposal-only stub: no specs, design or tasks yet. The binding
pattern is `openspec/changes/adopt-mapperly/design.md` (decisions D1–D10
and the Context wave table) — this wave copies that shape and does not
relitigate it.

## What Changes

- Scope is **the Scheduler view-projection code as it exists after
  `add-scheduled-jobs` lands** — the current inventory
  (`ScheduledJobView`, `ScheduledJobsPageView`, `ScheduledJobService`) is
  a pre-gate snapshot and may grow; the wave re-inventories at planning
  time. The module name for the mapper (`ISchedulerMapper` vs the
  ScheduledJobs naming `add-scheduled-jobs` introduces) follows the landed
  module shape.
- A singular scheduler mapper interface + `[Mapper(RequiredMappingStrategy
  = RequiredMappingStrategy.Target)] partial sealed class`, singleton in
  the module's application installer; `ScheduledJobView.Of`/`OfAll`
  migrate onto it. Note the namespace gotcha: `[Mapper]` and
  `RequiredMappingStrategy` live in `Riok.Mapperly.Abstractions`, not
  `Riok.Mapperly`.
- Page composition (`ScheduledJobsPageView`) is sized at planning time
  against the D4 boundary: pure entity→view projections migrate; any
  composition/aggregation shaping over query results may legitimately stay
  hand-written. The wave's design records which side of that line each
  member falls on.
- Scheduled job views migrate positional→init-property `required` records
  with **no wire change** — and, unlike Projects, `ScheduledJobView`
  carries per-field `<param>` XML docs: the doc text must move onto the
  properties so OpenAPI descriptions (and therefore the kubb client) do
  not drift (`adopt-mapperly/design.md` Context, XML-docs note).
- `MappingConventionTests` adopted-module list appends the Scheduler
  module's application namespace.
- The module csproj references the already-pinned `Riok.Mapperly` (CPM pin
  landed in `adopt-mapperly`) with `<PrivateAssets>all</PrivateAssets>` +
  `<ExcludeAssets>runtime</ExcludeAssets>`.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `backend-mapping` (created by `adopt-mapperly`): the adoption list widens
  to include the Scheduler module. No requirement text changes — the spec
  already names Scheduler as gated and each wave appends its module.

## Impact

- Backend only: the Scheduler module's Application layer (views, mapper +
  interface, DI installer, `ScheduledJobService`), the module's unit test
  project (mapper fidelity + wire pin),
  `tests/Comuki.Architecture.Tests` (one list entry).
- FE: none (kubb regen is a zero-diff verification gate, not an edit).
- No new endpoints, no permissions change, no breaking wire change.

## Non-goals

- Starting any implementation before `add-scheduled-jobs` lands — the gate
  is the first line of this proposal for a reason.
- Reopening any decision recorded in `adopt-mapperly/design.md` (D1–D10).
- Touching cron/timezone/overlap logic `add-scheduled-jobs` introduces —
  this wave only re-homes view projections.
- `RequiredMappingStrategy.None` or `[MapperIgnoreTarget]` escapes.
- Migrating other modules.
