import { describe, expect, it } from "vitest"

import {
  countNew,
  filterTasks,
  uniqueTaskApps,
} from "@/domains/tasks/model/filter-tasks"
import type { Task } from "@/domains/tasks/model/types"

const SAMPLE: Task[] = [
  {
    id: "COMUKI-128",
    source: "jira",
    title: "Кэш идемпотентных ответов",
    app: "billing-api",
    priority: "high",
    status: "new",
    age: "8 min",
  },
  {
    id: "m-3041",
    source: "manual",
    title: "Тёмная тема",
    app: "web-app",
    priority: "normal",
    status: "queued",
    age: "2 h",
  },
  {
    id: "COMUKI-124",
    source: "jira",
    title: "Rate-limit",
    app: "auth-svc",
    priority: "high",
    status: "planning",
    age: "3 h",
  },
]

describe("filterTasks", () => {
  it("filters by app and status", () => {
    const result = filterTasks(SAMPLE, {
      query: "",
      app: "billing-api",
      status: "new",
      priority: "all",
    })

    expect(result).toHaveLength(1)
    expect(result[0]?.id).toBe("COMUKI-128")
  })

  it("matches title query case-insensitively", () => {
    const result = filterTasks(SAMPLE, {
      query: "тёмная",
      app: "all",
      status: "all",
      priority: "all",
    })

    expect(result.map((task) => task.id)).toEqual(["m-3041"])
  })
})

describe("uniqueTaskApps / countNew", () => {
  it("lists unique apps sorted", () => {
    expect(uniqueTaskApps(SAMPLE)).toEqual([
      "auth-svc",
      "billing-api",
      "web-app",
    ])
  })

  it("counts new tasks", () => {
    expect(countNew(SAMPLE)).toBe(1)
  })
})
