import type {
  Task,
  TaskPriorityFilter,
  TaskStatusFilter,
} from "@/domains/tasks/model/types"
import type { ProjectRef } from "@/shared/session"

export interface TaskFilters {
  query: string
  /** A project id, or `all`. The backlog mixes projects like every list here. */
  project: string
  app: string
  status: TaskStatusFilter
  priority: TaskPriorityFilter
}

/**
 * The three fields the backlog's search box actually looks at.
 *
 * It is a function rather than three `includes` written twice because it *was*
 * written twice, and the two copies had already drifted: the column declared
 * `filter title, id, app…` and no `match`, so anything reading the declaration
 * — `applyDataFilters`, the toolbar's chip, any future consumer of
 * `dataFilterSpecs` — searched the title alone while this file searched all
 * three. A placeholder that promises more than the predicate delivers is a lie
 * the operator only catches by not finding a ticket they know exists. One
 * derivation, two consumers: `filterTasks` below and the `title` column's
 * `meta.filter.match`.
 */
export function matchesTaskQuery(task: Task, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) {
    return true
  }
  return (
    task.title.toLowerCase().includes(needle) ||
    task.id.toLowerCase().includes(needle) ||
    task.app.toLowerCase().includes(needle)
  )
}

export function uniqueTaskApps(tasks: Task[]): string[] {
  return [...new Set(tasks.map((task) => task.app))].sort()
}

/**
 * The project filter's option list: the projects the backlog actually holds
 * tickets for, resolved against the ones this session can see. Resolved rather
 * than derived, because a ticket carries an id and the filter has to offer a
 * key — `comuki`, not `p_comuki`.
 */
export function uniqueTaskProjects(
  tasks: Task[],
  projects: ProjectRef[]
): ProjectRef[] {
  const present = new Set(tasks.map((task) => task.projectId))
  return projects.filter((project) => present.has(project.id))
}

export function filterTasks(tasks: Task[], filters: TaskFilters): Task[] {
  return tasks.filter((task) => {
    if (filters.project !== "all" && task.projectId !== filters.project) {
      return false
    }
    if (filters.app !== "all" && task.app !== filters.app) {
      return false
    }
    if (filters.status !== "all" && task.status !== filters.status) {
      return false
    }
    if (filters.priority !== "all" && task.priority !== filters.priority) {
      return false
    }
    return matchesTaskQuery(task, filters.query)
  })
}

export function countNew(tasks: Task[]): number {
  return tasks.filter((task) => task.status === "new").length
}
