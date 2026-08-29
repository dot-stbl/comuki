# domains/approvals

## Purpose
Operational UI for **Approvals** in the Comuki dashboard.

## Routes
- See `src/routes/` — thin wrappers import pages from this domain.

## Public exports
- `ApprovalsPage` (and related pages) via `@/domains/approvals`.

## Invariants
- UI never imports Kubb DTO types directly — map in `api/` / `model/` first.
- Pages compose `AppShell` + domain UI; routes stay thin.
- Mock-first until W1–W3; no real list/detail until those slices.
