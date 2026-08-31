import { describe, expect, it } from "vitest"

import { toRunSummary } from "@/domains/runs/api/mappers"
import { isLongEdge, planGraph } from "@/domains/runs/model/work-items"
import type { RunSummary } from "@/domains/runs/model/types"
import { PROJECTS_SEED, RUNS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { can, type Session } from "@/shared/session"

/**
 * The seed is the only place the run graph's hard cases are exercised at real
 * scale, and every one of them is a shape the drawing has to survive rather
 * than a number worth pinning. So these assert that the shapes still EXIST —
 * a seed that quietly stopped producing long edges would take the screen's
 * whole justification with it and nothing else would notice.
 */
const runs = RUNS_SEED.map(toRunSummary)

describe("the plans the seed actually produces", () => {
  it("contains a plan the brain closed in a single chain of three", () => {
    const chains = runs.filter((run) => {
      const graph = planGraph(run.workItems)
      return (
        graph.columns.length === 3 &&
        graph.columns.every((column) => !column.parallel)
      )
    })

    expect(chains.length).toBeGreaterThan(0)
  })

  it("contains a plan that branches four wide", () => {
    const widest = Math.max(
      ...runs.flatMap((run) =>
        planGraph(run.workItems).columns.map((column) => column.items.length)
      )
    )

    // The tall-column rule exists for exactly this, so it must be reachable.
    expect(widest).toBeGreaterThanOrEqual(4)
  })

  it("contains a forty-plus item plan", () => {
    const biggest = Math.max(...runs.map((run) => run.workItems.length))
    expect(biggest).toBeGreaterThanOrEqual(40)
  })

  it("contains dependencies that skip a column — the named risk is real", () => {
    const withLongEdges = runs.filter((run) =>
      [...planGraph(run.workItems).dependencies.values()].some((deps) =>
        deps.some(isLongEdge)
      )
    )

    expect(withLongEdges.length).toBeGreaterThan(0)
  })

  it("contains items blocked behind something that stopped", () => {
    const blocked = runs.filter((run) => planGraph(run.workItems).blocked.size > 0)
    expect(blocked.length).toBeGreaterThan(0)
  })

  it("contains a run holding at a human gate mid-plan", () => {
    const waiting = runs.filter((run) =>
      run.workItems.some((item) => item.status === "waiting")
    )
    expect(waiting.length).toBeGreaterThan(0)
  })

  it("never asks the graph to draw a plan it cannot band", () => {
    for (const run of runs) {
      const graph = planGraph(run.workItems)
      const banded = graph.columns.reduce(
        (sum, column) => sum + column.items.length,
        0
      )
      // Every item lands in exactly one column, or the graph is losing work.
      expect(banded).toBe(run.workItems.length)
    }
  })
})

/**
 * The seeded shift, as the row-level permission rule sees it.
 *
 * The rule is only demonstrated if the swarm actually mixes projects and the
 * signed-in user actually holds different roles across them. A seed that
 * drifted into one project — or into three projects the session answers the
 * same way on — would leave the screen looking exactly like the old one where
 * a single check answered for everybody, and nothing else would notice.
 */
const SESSION: Session = { user: SESSION_USER_SEED, projects: PROJECTS_SEED }

/** The only two statuses whose row offers a decision at all. */
function needsHuman(run: RunSummary): boolean {
  return run.status === "waiting" || run.status === "escalated"
}

describe("the projects the seed spreads runs across", () => {
  it("puts every run in a project the shift can name", () => {
    const known = new Set(PROJECTS_SEED.map((project) => project.id))

    for (const run of runs) {
      expect(known.has(run.projectId)).toBe(true)
    }
  })

  it("works in all three, not in one with two decorations", () => {
    const perProject = new Map<string, number>()
    for (const run of runs) {
      perProject.set(run.projectId, (perProject.get(run.projectId) ?? 0) + 1)
    }

    expect([...perProject.keys()].sort()).toEqual([
      "p_atlas",
      "p_comuki",
      "p_plexor",
    ])
    // No project is a token presence: the smallest still carries a tenth of
    // the shift, so the mixed case is what the screen normally looks like.
    for (const count of perProject.values()) {
      expect(count).toBeGreaterThan(runs.length / 10)
    }
  })

  it("keeps one app in one project, so a row can never be in two", () => {
    const byApp = new Map<string, Set<string>>()
    for (const run of runs) {
      const seen = byApp.get(run.app) ?? new Set<string>()
      seen.add(run.projectId)
      byApp.set(run.app, seen)
    }

    for (const projects of byApp.values()) {
      expect(projects.size).toBe(1)
    }
  })

  it("offers the seeded shift a live approval and a refused one, side by side", () => {
    const gated = runs.filter(needsHuman)
    const live = gated.filter((run) =>
      can(SESSION, "plans.approve", run.projectId)
    )
    const refused = gated.filter(
      (run) => !can(SESSION, "plans.approve", run.projectId)
    )

    // Both piles have to be non-trivial, or the duty list stops making the
    // argument it exists to make.
    expect(live.length).toBeGreaterThan(3)
    expect(refused.length).toBeGreaterThan(3)
  })

  it("names the project in the refusal, and it is the one on the row", () => {
    const refused = runs.find(
      (run) => needsHuman(run) && !can(SESSION, "plans.approve", run.projectId)
    )

    // `p_plexor` is the one the seeded user only watches, so it is the key the
    // sentence has to carry — "needs approver … on plexor", never a flat no.
    expect(refused?.projectId).toBe("p_plexor")
  })
})
