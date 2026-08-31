import { describe, expect, it } from "vitest"

import {
  countNew,
  filterTasks,
  matchesTaskQuery,
  uniqueTaskApps,
  uniqueTaskProjects,
} from "@/domains/tasks/model/filter-tasks"
import type { Task } from "@/domains/tasks/model/types"
import type { ProjectRef } from "@/shared/session"

const PROJECTS: ProjectRef[] = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
]

const SAMPLE: Task[] = [
  {
    id: "COMUKI-128",
    projectId: "p_atlas",
    source: "jira",
    title: "Кэш идемпотентных ответов",
    app: "billing-api",
    priority: "high",
    status: "new",
    age: "8 min",
  },
  {
    id: "m-3041",
    projectId: "p_comuki",
    source: "manual",
    title: "Тёмная тема",
    app: "web-app",
    priority: "normal",
    status: "queued",
    age: "2 h",
  },
  {
    id: "COMUKI-124",
    projectId: "p_plexor",
    source: "jira",
    title: "Rate-limit",
    app: "auth-svc",
    priority: "high",
    status: "planning",
    age: "3 h",
  },
]

/** No filter at all — the shape every case below starts from. */
const NONE = {
  query: "",
  project: "all",
  app: "all",
  status: "all",
  priority: "all",
} as const

describe("filterTasks", () => {
  it("filters by app and status", () => {
    const result = filterTasks(SAMPLE, {
      ...NONE,
      app: "billing-api",
      status: "new",
    })

    expect(result).toHaveLength(1)
    expect(result[0]?.id).toBe("COMUKI-128")
  })

  it("matches title query case-insensitively", () => {
    const result = filterTasks(SAMPLE, { ...NONE, query: "тёмная" })

    expect(result.map((task) => task.id)).toEqual(["m-3041"])
  })

  it("filters by project id, the value the row carries", () => {
    const result = filterTasks(SAMPLE, { ...NONE, project: "p_plexor" })

    expect(result.map((task) => task.id)).toEqual(["COMUKI-124"])
  })

  it("leaves the backlog whole when no project is chosen", () => {
    expect(filterTasks(SAMPLE, NONE)).toHaveLength(3)
  })
})

describe("uniqueTaskApps / uniqueTaskProjects / countNew", () => {
  it("lists unique apps sorted", () => {
    expect(uniqueTaskApps(SAMPLE)).toEqual([
      "auth-svc",
      "billing-api",
      "web-app",
    ])
  })

  it("offers the projects the backlog actually holds tickets for", () => {
    expect(
      uniqueTaskProjects(SAMPLE.slice(0, 2), PROJECTS).map((entry) => entry.key)
    ).toEqual(["comuki", "atlas"])
  })

  it("counts new tasks", () => {
    expect(countNew(SAMPLE)).toBe(1)
  })
})

describe("matchesTaskQuery", () => {
  it("matches the title, the tracker id and the app", () => {
    const [ticket] = SAMPLE

    expect(matchesTaskQuery(ticket, "кэш")).toBe(true)
    expect(matchesTaskQuery(ticket, "COMUKI-128")).toBe(true)
    expect(matchesTaskQuery(ticket, "billing-api")).toBe(true)
  })

  it("is case-insensitive and ignores surrounding space", () => {
    expect(matchesTaskQuery(SAMPLE[2], "  rATe  ")).toBe(true)
    expect(matchesTaskQuery(SAMPLE[2], "comuki-124")).toBe(true)
  })

  it("keeps every ticket when there is nothing to look for", () => {
    expect(SAMPLE.every((task) => matchesTaskQuery(task, "   "))).toBe(true)
  })

  it("says no when the needle is in none of the three fields", () => {
    expect(matchesTaskQuery(SAMPLE[0], "docs-site")).toBe(false)
  })
})
