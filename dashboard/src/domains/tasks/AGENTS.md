# domains/tasks

## Purpose
Operational UI for **Tasks** in the Comuki dashboard.

## Routes
- See `src/routes/` — thin wrappers import pages from this domain.

## Public exports
- `TasksPage` (and related pages) via `@/domains/tasks`.

## Invariants
- UI never imports Kubb DTO types directly — map in `api/` / `model/` first.
- Pages compose `AppShell` + domain UI; routes stay thin.
- Mock-first until W1–W3; no real list/detail until those slices.
- **Permission is a row question, not a session one.** A task carries
  `projectId`; dispatch asks `can(session, "inbox.take", task.projectId)` and
  the denial names that project's key. The `New task` opener asks the wider
  question with no project — may this shift take work *anywhere* — because
  hiding intake from someone who runs it on one project of three is a lie of
  omission. The dialog then offers only the projects that answer yes.
