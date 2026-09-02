import { describe, expect, it } from "vitest"

import {
  outcomeDayTotal,
  outcomesNotCovering,
  outcomeWindowTotal,
  toOutcomeDays,
} from "@/domains/home/model/outcomes"
import type { RunSummary } from "@/domains/runs/model/types"
import { OUTCOMES_SEED, RUNS_SEED } from "@/shared/api/mock/runs.seed"

function run(id: string, status: RunSummary["status"]): RunSummary {
  return {
    id,
    projectId: "p_test",
    app: "billing-api",
    title: `ticket ${id}`,
    status,
    current: "w1",
    model: "worker",
    cost: 0.4,
    tokens: 8000,
    durationSec: 300,
    done: status === "success",
    workItems: [],
  }
}

const days = toOutcomeDays(OUTCOMES_SEED)

describe("the day's arithmetic", () => {
  it("sums a day's finished runs across the stack", () => {
    expect(outcomeDayTotal(days[days.length - 1])).toBe(26 + 12 + 9)
    expect(outcomeDayTotal({ label: "quiet", outcomes: [] })).toBe(0)
  })

  it("totals one status across the window", () => {
    const failed = days.reduce(
      (sum, day) =>
        sum + (day.outcomes.find((entry) => entry.status === "failed")?.count ?? 0),
      0
    )
    expect(outcomeWindowTotal(days, "failed")).toBe(failed)
    expect(outcomeWindowTotal([], "failed")).toBe(0)
  })

  it("keeps the stack order it was seeded with — success at the base", () => {
    for (const day of days) {
      expect(day.outcomes[0]?.status).toBe("success")
      // And only the three statuses a run can rest in overnight.
      for (const entry of day.outcomes) {
        expect(["success", "failed", "escalated"]).toContain(entry.status)
      }
    }
  })
})

describe("the seeded week tells the seeded story", () => {
  it("carries seven days, oldest first, today last", () => {
    expect(days).toHaveLength(7)
    expect(days[days.length - 1]?.label).toBe("today")
    expect(new Set(days.map((day) => day.label)).size).toBe(7)
  })

  it("puts the failure spike where the incident story says one is", () => {
    // Three days back the auth-svc migration ran: the cost seed spikes on the
    // same day, and the app list still carries its +21% trend. One incident,
    // three readings — not three coincidences.
    const incident = days.find((_, index) => index === 3)
    const incidentFailed =
      incident?.outcomes.find((entry) => entry.status === "failed")?.count ?? 0
    const otherFailed = days
      .filter((_, index) => index !== 3)
      .map(
        (day) =>
          day.outcomes.find((entry) => entry.status === "failed")?.count ?? 0
      )

    expect(incidentFailed).toBeGreaterThan(Math.max(...otherFailed))
  })

  it("keeps the weekend columns lighter than the weekdays", () => {
    // The seed labels real weekdays off the clock, so whichever days are the
    // weekend this week, their totals must sit under every working day's.
    const weekend = days
      .filter((day) => day.label === "sat" || day.label === "sun")
      .map(outcomeDayTotal)
    const working = days
      .filter(
        (day) => day.label !== "sat" && day.label !== "sun" && day.label !== "today"
      )
      .map(outcomeDayTotal)

    expect(weekend.length).toBeGreaterThanOrEqual(1)
    expect(Math.max(...weekend)).toBeLessThan(Math.min(...working))
  })

  it("never finishes fewer runs today than the live list is showing", () => {
    // The list is today's shift: ages are minutes, leases are minutes. A
    // history that finished fewer runs than the list holds would describe a
    // different day than the one on the screen above it.
    expect(outcomesNotCovering(days, RUNS_SEED)).toEqual([])
  })

  it("flags exactly the statuses a too-small today would hide", () => {
    const listed = [
      run("f1", "failed"),
      run("f2", "failed"),
      run("f3", "failed"),
    ]
    const thin = [
      { label: "mon", outcomes: [{ status: "failed" as const, count: 9 }] },
      { label: "today", outcomes: [{ status: "failed" as const, count: 2 }] },
    ]

    expect(outcomesNotCovering(thin, listed)).toEqual(["failed"])
    expect(outcomesNotCovering([], listed)).toEqual([
      "success",
      "failed",
      "escalated",
    ])
  })
})
