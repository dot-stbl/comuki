import { describe, expect, it } from "vitest"

import { uniqueApps, uniqueProjects } from "@/domains/runs/model/filter-runs"
import type { RunSummary } from "@/domains/runs/model/types"
import type { ProjectRef } from "@/shared/session"

const PROJECTS: ProjectRef[] = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
]

function run(id: string, app: string, projectId = "p_comuki"): RunSummary {
  return {
    id,
    projectId,
    app,
    title: `run ${id}`,
    status: "running",
    current: "w1",
    model: "worker",
    cost: 0,
    tokens: 0,
    durationSec: 0,
    done: false,
    workItems: [],
  }
}

describe("uniqueApps", () => {
  it("returns each app once, sorted", () => {
    const runs = [
      run("a", "web-app"),
      run("b", "billing-api"),
      run("c", "web-app"),
    ]

    expect(uniqueApps(runs)).toEqual(["billing-api", "web-app"])
  })

  it("returns nothing for an empty swarm", () => {
    expect(uniqueApps([])).toEqual([])
  })
})

describe("uniqueProjects", () => {
  it("offers only the projects the swarm is actually working in", () => {
    const runs = [
      run("a", "web-app", "p_comuki"),
      run("b", "auth-svc", "p_plexor"),
      run("c", "worker-pool", "p_comuki"),
    ]

    // Atlas is a project this session can see and the list has no run in it,
    // so it is not an option — a filter that can only ever return nothing is
    // not a filter.
    expect(uniqueProjects(runs, PROJECTS).map((entry) => entry.key)).toEqual([
      "comuki",
      "plexor",
    ])
  })

  it("keeps the platform's own order rather than inventing a second one", () => {
    const runs = [run("a", "docs-site", "p_atlas"), run("b", "web-app")]

    expect(uniqueProjects(runs, PROJECTS).map((entry) => entry.id)).toEqual([
      "p_comuki",
      "p_atlas",
    ])
  })

  it("drops a run in a project this session cannot see", () => {
    // The row still renders — the column says so with a dash — but a project
    // the session has no reference for cannot become a filter option.
    const runs = [run("a", "ghost-svc", "p_unknown")]

    expect(uniqueProjects(runs, PROJECTS)).toEqual([])
  })
})
