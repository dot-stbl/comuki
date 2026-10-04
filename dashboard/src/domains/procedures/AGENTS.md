# domains/procedures

## Purpose
Operational UI for the procedure workbench — `Studio / Live run / Replay`
panels for a procedure, against the procedures surface on the host
(tasks 7.1+7.2; foundation: master d74b64ae commit `939a4d91`).

## Routes
- `src/routes/procedures/$procedureKey.tsx` — thin wrapper that re-exports
  the page (file-based routing; the route id is `/procedures/$procedureKey`,
  the mode panel reads `?mode=studio|live|replay`).

## Public exports
- `ProceduresPage` via `@/domains/procedures`.
- Query / mutation hooks (`useLatestProcedureVersionQuery`, `useProposePatchMutation`,
  …) — used by the workbench component when wired against real mode.

## Panel choice — single page, mode from URL (decision, 2026-10-04)
The workbench ships **one route with three panels** instead of three
separate pages. The reasoning:

- The three panels read the same procedure, the same graph, the same
  header, the same vocabulary. Splitting them into three pages means
  re-loading the chrome three times for one operator decision ("is this
  procedure in a good shape right now?").
- The studio / live / replay distinction is a *panel choice*, not a
  *route choice* — operators move between them inside one session, not
  as separate screens they bookmark.
- The mode selector lives on `?mode=`, the canonical "filter the
  same view" surface. Bookmarks survive (`?mode=replay&runId=…`); the
  back button stays inside the procedure.

Trade-off accepted: the URL is a bit longer (`?mode=…`); the
alternative was three routes under `/procedures/$procedureKey/{studio|live|replay}`,
which would surface as three siblings in the nav and force the operator
to leave a panel to reach another. The nav lists **procedures once**;
the mode strip lives inside the page.

## Invariants
- UI never imports Kubb DTO types directly — map in `api/` / `model/` first.
- Pages compose `AppShell` (`PageTemplate`) + domain UI; routes stay
  thin (file-based wrappers).
- The wire's `JsonElement` payloads on the publish body stay opaque —
  `api/mutations.ts` sends the body verbatim; the host
  (`Comuki.Host/Procedures/Helpers/PublicationRequestMapper.cs`) is the
  only place that deserialises the typed payload.
- Mock-first today (`VITE_USE_MOCK=true`): the page renders the
  canonical fixtures from `model/procedure-fixtures.ts`. Real mode
  (PHASE 2 follow-up) will route the queries through
  `useLatestProcedureVersionQuery` + `useProcedureTraceQuery` etc.

## The procedure model — the one thing not to get wrong
A procedure's graph is an arbitrary **DAG of typed nodes**, never a
fixed pipeline. Nothing may assume a stage catalog, an item count, or a
shape — only the canonical `Intake / Plan / Execute / Verify / Repair
loop / Human gate` reading the storybook fixtures carry.

- `ProcedureNode.kindKey` is the **identity**: a closed catalog
  declared in the control-plane git (`procedure-node-kinds`). Aggregate,
  filter, sort and route on this.
- `ProcedureNode.parameters` are the kind-declared inputs the procedure
  may override. Stored as a string map for v1.

`model/types.ts` owns the domain types and the wire vocabulary; the
workbench (`procedure-workbench.tsx`) reads only those — no kubb types
cross the boundary into the canvas.