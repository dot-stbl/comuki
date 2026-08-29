import { describe, expect, it } from "vitest"

import {
  countActive,
  filterRuns,
  uniqueApps,
} from "@/domains/runs/model/filter-runs"
import type { RunSummary } from "@/domains/runs/model/types"

const sample: RunSummary[] = [
  {
    id: "aaa",
    app: "web-app",
    title: "Dark theme",
    status: "running",
    current: "front",
    model: "worker",
    cost: 0.1,
    tokens: 1000,
    durationSec: 10,
    done: false,
    stages: [],
  },
  {
    id: "bbb",
    app: "billing-api",
    title: "Webhook retry",
    status: "queued",
    current: "explore",
    model: "worker",
    cost: 0,
    tokens: 0,
    durationSec: 0,
    done: false,
    stages: [],
  },
  {
    id: "ccc",
    app: "web-app",
    title: "Virtual list",
    status: "escalated",
    current: "sync",
    model: "lead",
    cost: 1,
    tokens: 5000,
    durationSec: 40,
    done: false,
    stages: [],
  },
]

describe("filterRuns", () => {
  it("filters by app and status", () => {
    const result = filterRuns(sample, {
      query: "",
      app: "web-app",
      status: "running",
    })
    expect(result).toHaveLength(1)
    expect(result[0]?.id).toBe("aaa")
  })

  it("matches query against id title and app", () => {
    const result = filterRuns(sample, {
      query: "billing",
      app: "all",
      status: "all",
    })
    expect(result.map((run) => run.id)).toEqual(["bbb"])
  })
})

describe("uniqueApps / countActive", () => {
  it("lists apps sorted", () => {
    expect(uniqueApps(sample)).toEqual(["billing-api", "web-app"])
  })

  it("counts running and escalated", () => {
    expect(countActive(sample)).toBe(2)
  })
})
