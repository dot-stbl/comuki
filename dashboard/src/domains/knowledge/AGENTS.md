# domains/knowledge

## Purpose
Operational UI for **Knowledge** in the Comuki dashboard.

## Screen shape
- One screen, two tabs (`?tab=`, default `library`): `library` (revision
  readings, rules/docs/skills list with detail sheet, golden tasks) and `gate`
  (the per-project verification gate, folded in from the retired `/verify`
  screen — panels come from `@/domains/verify/ui/verify-project-panel`).
- Folded-section rule: the route gates `knowledge.view` (the door); the gate
  tab asks `verify.view` of its own and is **hidden, never disabled** when the
  answer is no. Same rule as the boards section on Compute.
- `/verify` is a redirect stub to `/knowledge?tab=gate` (`src/routes/verify.tsx`).
- Tab list/validation: `model/tabs.ts` (`isKnowledgeTab`), consumed by the
  route's `validateSearch`; unknown/missing `tab` → `library`.

## Routes
- See `src/routes/` — thin wrappers import pages from this domain.

## Public exports
- `KnowledgePage`, `isKnowledgeTab` / `KnowledgeTab` via `@/domains/knowledge`.

## Invariants
- UI never imports Kubb DTO types directly — map in `api/` / `model/` first.
- Pages compose `AppShell` + domain UI; routes stay thin.
- Mock-first until W1–W3; no real list/detail until those slices.
