import { describe, expect, it } from "vitest"

import { toTask } from "@/domains/tasks/api/mappers"
import { uniqueTaskApps, uniqueTaskProjects } from "@/domains/tasks/model/filter-tasks"
import { createTaskColumns } from "@/domains/tasks/ui/tasks-columns"
import { PROJECTS_SEED, SESSION_USER_SEED, TASKS_SEED } from "@/shared/api/mock"
import type { Session } from "@/shared/session"
import {
  applyDataFilters,
  dataFilterSpecs,
  type DataColumnFilter,
} from "@/shared/ui"

/**
 * The columns take the session as a value rather than through a hook — which is
 * what lets this file build them with no React at all. The permission questions
 * are `tasks-gate.test.tsx`; here it is the declarations, and in particular the
 * one that used to lie.
 */
const session: Session = {
  user: SESSION_USER_SEED,
  projects: PROJECTS_SEED,
}

const tasks = TASKS_SEED.map(toTask)
const columns = createTaskColumns({
  apps: uniqueTaskApps(tasks),
  projects: uniqueTaskProjects(tasks, PROJECTS_SEED),
  dispatching: false,
  onDispatch: () => {},
  session,
})

function filterOf(id: string): DataColumnFilter<never> | undefined {
  return dataFilterSpecs(columns).find((entry) => entry.id === id)?.filter as
    | DataColumnFilter<never>
    | undefined
}

describe("the backlog's search filter", () => {
  it("says it reads three fields", () => {
    const filter = filterOf("title")
    expect(filter?.kind).toBe("text")
    expect(filter?.placeholder).toBe("filter title, id, app…")
  })

  /**
   * The regression this file exists for. The column declared the placeholder
   * above and no `match`, so `applyDataFilters` fell back to comparing the
   * column's own `title` field — the box promised an id search and an app
   * search it never ran, and an operator only finds that out by failing to
   * locate a ticket they know is in the list.
   */
  it("finds a ticket by its tracker id, not only by its title", () => {
    const ticket = tasks.find((task) => task.source === "jira")
    expect(ticket).toBeDefined()

    const rows = applyDataFilters(tasks, { title: ticket!.id }, columns)
    expect(rows.map((task) => task.id)).toContain(ticket!.id)
  })

  it("finds every ticket for an app by the app's name", () => {
    const app = tasks[0].app
    const expected = tasks.filter((task) => task.app === app).map((t) => t.id)

    const rows = applyDataFilters(tasks, { title: app }, columns)
    expect(rows.map((task) => task.id)).toEqual(expected)
  })

  it("still matches a title, case-insensitively", () => {
    const needle = tasks[0].title.slice(0, 6).toUpperCase()
    const rows = applyDataFilters(tasks, { title: needle }, columns)

    expect(rows.map((task) => task.id)).toContain(tasks[0].id)
  })
})

describe("the backlog's select filters", () => {
  it("offers each project by id and reads back the key", () => {
    const filter = filterOf("project")
    expect(filter?.kind).toBe("select")

    const options =
      filter?.kind === "select" ? filter.options.map((o) => o.value) : []
    for (const projectId of options) {
      const rows = applyDataFilters(tasks, { project: projectId }, columns)
      expect(rows.length).toBeGreaterThan(0)
      expect(rows.every((task) => task.projectId === projectId)).toBe(true)
    }
  })

  it("offers every app the backlog actually holds tickets for", () => {
    const filter = filterOf("app")
    const options =
      filter?.kind === "select" ? filter.options.map((o) => o.value) : []

    expect(options).toEqual(uniqueTaskApps(tasks))
  })
})
