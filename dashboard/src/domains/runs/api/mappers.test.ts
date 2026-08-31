import { describe, expect, it } from "vitest"

import { toRunSummary } from "@/domains/runs/api/mappers"
import { buildProfileFlow } from "@/domains/runs/model/profile-flow"
import { itemDepths } from "@/domains/runs/model/work-items"
import { PROFILE_CATALOG, RUNS_SEED } from "@/shared/api/mock"

/**
 * The seeded shift, as a contract.
 *
 * The screen's whole thesis is that a plan is an arbitrary graph and the step
 * names are prose. A mock that quietly settled back into one fixed shape, or
 * into one label per profile, would let a hardcoded assumption reappear and
 * still look green. These assertions are what stop that.
 */

const runs = RUNS_SEED.map(toRunSummary)

describe("the seeded shift", () => {
  it("is the load the duty screen is designed for", () => {
    expect(runs.length).toBeGreaterThanOrEqual(50)
    expect(runs.length).toBeLessThanOrEqual(200)
  })

  it("holds genuinely different graph shapes", () => {
    const sizes = runs.map((run) => run.workItems.length)

    // The brain closed some tickets without planning them at all.
    expect(sizes.filter((size) => size === 3).length).toBeGreaterThan(0)
    // Most plans are ordinary.
    expect(
      sizes.filter((size) => size >= 8 && size <= 15).length
    ).toBeGreaterThan(20)
    // And nothing in the product may assume a run is small.
    expect(Math.max(...sizes)).toBeGreaterThanOrEqual(40)
  })

  it("branches, and never more than four lanes wide", () => {
    let widest = 0
    for (const run of runs) {
      const depths = itemDepths(run.workItems)
      const perDepth = new Map<number, number>()
      for (const item of run.workItems) {
        const depth = depths.get(item.id) ?? 0
        perDepth.set(depth, (perDepth.get(depth) ?? 0) + 1)
      }
      widest = Math.max(widest, ...perDepth.values())
    }

    expect(widest).toBeGreaterThan(1)
    expect(widest).toBeLessThanOrEqual(4)
  })

  it("invokes only profiles the client has declared", () => {
    const declared = new Set<string>(PROFILE_CATALOG)
    const used = new Set(
      runs.flatMap((run) => run.workItems.map((item) => item.profile))
    )

    for (const profile of used) {
      expect(declared.has(profile)).toBe(true)
    }
    expect(used.size).toBe(declared.size)
  })

  it("gives the same profile a different step name on different tickets", () => {
    const labels = new Set(
      runs.flatMap((run) =>
        run.workItems
          .filter((item) => item.profile === "implementer")
          .map((item) => item.label)
      )
    )

    expect(labels.size).toBeGreaterThan(8)
  })

  it("points every run at a work item that exists", () => {
    for (const run of runs) {
      const ids = new Set(run.workItems.map((item) => item.id))
      expect(ids.size).toBe(run.workItems.length)
      expect(ids.has(run.current)).toBe(true)
      for (const item of run.workItems) {
        for (const dependency of item.dependsOn) {
          expect(ids.has(dependency)).toBe(true)
        }
      }
    }
  })

  it("derives a board whose columns nobody wrote down", () => {
    const flow = buildProfileFlow(runs)

    expect(flow.order).toEqual([
      "explorer",
      "planner",
      "implementer",
      "reviewer",
      "tester",
      "verifier",
      "docs",
    ])
    // Work leaves each gap in smaller numbers than it entered the one before:
    // the board draws a funnel because the runs make one, not because we said so.
    expect(flow.crossings).toEqual([...flow.crossings].sort((a, b) => b - a))
    expect(flow.pinchProfile).toBe("verifier")
  })
})
