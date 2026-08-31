# domains/runs

## Purpose
Operational UI for **Live runs** in the Comuki dashboard.

## Routes
- See `src/routes/` — thin wrappers import pages from this domain.

## Public exports
- `RunsPage` (and related pages) via `@/domains/runs`.

## Invariants
- UI never imports Kubb DTO types directly — map in `api/` / `model/` first.
- Pages compose `AppShell` + domain UI; routes stay thin.
- Mock-first until W1–W3; no real list/detail until those slices.
- **Permission is a row question, not a session one.** A run carries
  `projectId`; approve and cancel ask `can(session, …, run.projectId)` and the
  denial names that project's key. A `useCan` at the top of the page answers
  for the wrong thing, and a `useCan` inside a `cell` is a hook outside a
  render — the page passes the `Session` down and the cell calls `can()`.

## The plan model — the one thing not to get wrong
A run's plan is an arbitrary **graph of work items**, never a fixed pipeline.
Nothing may assume a stage catalog, an item count or a shape.

- `WorkItem.profile` is the **identity**: a closed catalog declared in the
  client's git. Aggregate, filter, sort and route on this.
- `WorkItem.label` is the **brain's own name** for the step, invented per
  ticket. Show it — it is the most human thing on a row — and never aggregate,
  key, group or bucket on it.

`model/profile-flow.ts` is the board's aggregate: nodes per profile, edges as
observed transitions, column order derived from median depth. `model/
work-items.ts` owns depth and dependency order; every graph reader uses it.

`planGraph()` adds the two facts a *drawing* needs and a list does not, and
they live in the model because they are graph readings, not view state:

- **span** — how many columns a dependency crosses. The run graph lays items
  out in depth bands, which promises "a connection joins the band beside you".
  A span of 2+ breaks that promise, so it is measured and marked rather than
  quietly lost. 66% of the seed's runs contain at least one.
- **blocked** — a queued item with a `failed` or `escalated` ancestor. Its own
  status says `queued`, which is true and misleading: nothing will happen until
  a person looks upstream. `waiting` does *not* propagate — a human gate in its
  normal state is its own reading, not a fault.
