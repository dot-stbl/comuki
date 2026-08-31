import { describe, expect, it } from "vitest"

import type { SeedProject } from "@/shared/api/mock/projects.seed"

import { buildProjectRows, type CostFact, type RunFact } from "./activity"

const PROJECTS: SeedProject[] = [
  {
    id: "p_atlas",
    slug: "atlas",
    name: "Atlas",
    gitProfileRepo: null,
    createdAt: "2026-06-27",
  },
  {
    id: "p_vega",
    slug: "vega",
    name: "Vega",
    gitProfileRepo: null,
    createdAt: "2026-08-28",
  },
]

const RUNS: RunFact[] = [
  { projectId: "p_atlas", app: "billing-api", status: "running" },
  { projectId: "p_atlas", app: "billing-api", status: "success" },
  { projectId: "p_atlas", app: "checkout-web", status: "waiting" },
]

const COSTS: CostFact[] = [
  { app: "billing-api", spend: 52.4 },
  { app: "checkout-web", spend: 10.1 },
  // An app nobody has run: there is no project to attribute it to, and
  // guessing one would put a number on the wrong row.
  { app: "orphan-svc", spend: 99 },
]

describe("what a project is doing", () => {
  it("counts every run and the ones still in flight apart", () => {
    const [atlas] = buildProjectRows(PROJECTS, RUNS, COSTS)

    expect(atlas.totalRuns).toBe(3)
    expect(atlas.activeRuns).toBe(2)
  })

  it("attributes spend through the apps the runs name", () => {
    const [atlas] = buildProjectRows(PROJECTS, RUNS, COSTS)

    expect(atlas.spendToday).toBeCloseTo(62.5)
  })

  it("leaves a project nothing has been measured against unmeasured", () => {
    const [, vega] = buildProjectRows(PROJECTS, RUNS, COSTS)

    // `null`, not zero: a project created this morning has not spent nothing,
    // it has not been measured — and the column renders a dash for it.
    expect(vega.spendToday).toBeNull()
    expect(vega.totalRuns).toBe(0)
    expect(vega.activeRuns).toBe(0)
  })

  it("drops a fact it cannot read rather than the row carrying it", () => {
    // The run seed is written by other hands and gains fields as it goes. A row
    // missing the one field this module reads must degrade, not throw.
    const rows = buildProjectRows(
      PROJECTS,
      [{ app: "billing-api", status: "running" }, { projectId: "p_atlas" }],
      [{ app: "billing-api", spend: 5 }, { spend: 3 }, { app: "billing-api" }]
    )

    expect(rows).toHaveLength(2)
    expect(rows[0].totalRuns).toBe(1)
    expect(rows[0].activeRuns).toBe(0)
    // The app never learned an owner, so its spend went nowhere.
    expect(rows[0].spendToday).toBeNull()
  })
})
