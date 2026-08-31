import type { RunSummary } from "@/domains/runs/model/types"
import type { ProjectRef } from "@/shared/session"

/**
 * The app filter's option list.
 *
 * Filtering itself moved to the table: `meta.filter` is declared once on the
 * column and `applyDataFilters` evaluates it, so the old `filterRuns` and
 * `countActive` here were dead the moment the board stopped deciding what the
 * list contained. They are gone rather than kept warm by their own tests.
 */
export function uniqueApps(runs: RunSummary[]): string[] {
  return [...new Set(runs.map((run) => run.app))].sort()
}

/**
 * The project filter's option list: the projects the swarm is actually working
 * in, resolved against the ones this session can see.
 *
 * Resolved rather than derived, because a run carries an id and the filter has
 * to offer a *key* — `comuki`, not `p_comuki`. The session's own order is kept:
 * it is the platform's list of projects, and re-sorting it here would invent a
 * second order for the same set. A run in a project the session cannot see
 * contributes no option; the column still says so on the row.
 */
export function uniqueProjects(
  runs: RunSummary[],
  projects: ProjectRef[]
): ProjectRef[] {
  const present = new Set(runs.map((run) => run.projectId))
  return projects.filter((project) => present.has(project.id))
}
