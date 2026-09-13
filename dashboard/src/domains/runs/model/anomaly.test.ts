import { describe, expect, it } from "vitest"

import { ANOMALY_MULTIPLIER, anomalyFor, flagAnomalies } from "./anomaly"
import type { RunSummary } from "./types"

function run(
  id: string,
  projectId: string,
  cost: number,
  overrides: Partial<RunSummary> = {}
): RunSummary {
  return {
    id,
    projectId,
    app: "billing-api",
    title: `run ${id}`,
    status: "running",
    current: "w1",
    model: "worker",
    cost,
    tokens: 1000,
    durationSec: 60,
    done: false,
    workItems: [],
    anomaly: null,
    ...overrides,
  }
}

describe("the anomaly rule", () => {
  it("flags a run whose cost crosses the 3× project median", () => {
    const runs = [
      run("a", "p_test", 1),
      run("b", "p_test", 1),
      run("c", "p_test", 1),
      // Median of [1,1,1,5] = 1; 5 = 5× the median — flagged.
      run("spike", "p_test", 5),
    ]

    const flags = flagAnomalies(runs)
    expect(flags.size).toBe(1)
    expect(flags.get("spike")?.multiplier).toBe(5)
    expect(flags.get("spike")?.reason).toBe("cost-spike")
  })

  it("does not flag a heavy run whose whole project is heavy", () => {
    // The seed's prometheus — every run in this project is expensive; the
    // rule fires on per-project outliers, not on absolute spend.
    const runs = Array.from({ length: 5 }, (_, index) =>
      run(`heavy-${index}`, "p_prometheus", 4)
    )
    const flags = flagAnomalies(runs)
    expect(flags.size).toBe(0)
  })

  it("computes the median per project, not across the swarm", () => {
    // comuki runs are cheap; one expensive run in comuki is the spike.
    // atlas runs are mid; one expensive run in atlas is the spike.
    const runs = [
      run("c1", "p_comuki", 1),
      run("c2", "p_comuki", 1),
      run("c-spike", "p_comuki", 4),
      run("a1", "p_atlas", 2),
      run("a2", "p_atlas", 2),
      run("a-spike", "p_atlas", 8),
    ]

    const flags = flagAnomalies(runs)
    expect(flags.size).toBe(2)
    expect(flags.get("c-spike")?.multiplier).toBe(4)
    expect(flags.get("a-spike")?.multiplier).toBe(4)
  })

  it("returns no flag for a project whose runs are all zero", () => {
    // Median of [0, 0, 0] is 0, and the rule treats a zero-median project
    // as "nothing to compare against" rather than firing on every row.
    const runs = [run("a", "p_zero", 0), run("b", "p_zero", 0)]
    const flags = flagAnomalies(runs)
    expect(flags.size).toBe(0)
  })

  it("exposes the threshold constant for the row's tooltip", () => {
    expect(ANOMALY_MULTIPLIER).toBe(3)
  })
})

describe("anomalyFor", () => {
  it("answers one run against a hand-picked peer set", () => {
    const peer = [1, 1, 1, 1]
    const candidate = run("x", "p_test", 5)
    const flag = anomalyFor(candidate, peer)
    expect(flag).not.toBeNull()
    expect(flag?.multiplier).toBe(5)
  })

  it("returns null when the run sits at or under the threshold", () => {
    const peer = [1, 1, 1, 1]
    const candidate = run("x", "p_test", 3)
    expect(anomalyFor(candidate, peer)).toBeNull()
  })

  it("returns null when there is nothing to compare against", () => {
    expect(anomalyFor(run("x", "p_test", 5), [])).toBeNull()
  })
})
