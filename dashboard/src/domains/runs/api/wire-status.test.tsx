import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import {
  mapRunViewToSummary,
  normalizeRunStatus,
} from "@/domains/runs/api/mappers"
import { TRIAGE_RANK, triageOrder } from "@/domains/runs/model/profile-flow"
import type { RunSummary } from "@/domains/runs/model/types"
import type { RunView } from "@/shared/api/_generated/types/RunView"
import { StatusBadge } from "@/shared/ui"

/**
 * The seam between the host's seven lifecycle words and the design system's
 * six.
 *
 * The wire carries `succeeded` and `cancelled`; `RunStatus` has neither. A
 * cast used to carry them across, and two consumers paid for it at runtime —
 * `StatusBadge` looked an icon up by status and got `undefined` (React then
 * throws "Element type is invalid", so the first completed run in real mode
 * took the page down), and `TRIAGE_RANK` looked a rank up and got `NaN`, which
 * does not throw at all — it just quietly stops the duty list from sorting.
 *
 * Both halves are asserted here, because only one of them announces itself.
 */

/** The kit marks its badges with `data-test`, not `data-testid`. */
function renderedBadge(): HTMLElement {
  const badge = document.querySelector<HTMLElement>(
    '[data-test="status-badge"]',
  )
  if (badge === null) {
    throw new Error("no status badge was rendered")
  }
  return badge
}

function runViewFixture(overrides: Partial<RunView> = {}): RunView {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    projectId: "00000000-0000-0000-0000-0000000000aa",
    status: "running",
    createdAt: "2026-09-04T10:00:00.000+00:00",
    updatedAt: "2026-09-04T10:05:30.000+00:00",
    ...overrides,
  }
}

describe("normalizeRunStatus", () => {
  it("passes the six design-system words through unchanged", () => {
    expect(normalizeRunStatus("running")).toBe("running")
    expect(normalizeRunStatus("success")).toBe("success")
    expect(normalizeRunStatus("failed")).toBe("failed")
    expect(normalizeRunStatus("waiting")).toBe("waiting")
    expect(normalizeRunStatus("queued")).toBe("queued")
    expect(normalizeRunStatus("escalated")).toBe("escalated")
  })

  it("spells the host's `succeeded` as the product's `success`", () => {
    expect(normalizeRunStatus("succeeded")).toBe("success")
  })

  it("degrades `cancelled` to the safest existing word rather than inventing a seventh", () => {
    // Provisional, and deliberately not `success`: a stopped run did not land
    // its work. `failed` is the only remaining word that is both terminal and
    // not a success. The real answer is a DESIGN.md decision (a seventh status
    // needs a hue *and* a hatch); until it is made the mapper must not coin one.
    expect(normalizeRunStatus("cancelled")).toBe("failed")
  })

  it("degrades anything it has never been taught to the same fallback", () => {
    // A partial backend rollout degrades the row, not the screen.
    expect(normalizeRunStatus("paused")).toBe("failed")
    expect(normalizeRunStatus("")).toBe("failed")
    expect(normalizeRunStatus("SUCCEEDED")).toBe("failed")
  })
})

describe("a `succeeded` run off the wire", () => {
  it("maps to a status the badge can actually draw", () => {
    const summary = mapRunViewToSummary(runViewFixture({ status: "succeeded" }))

    expect(summary.status).toBe("success")
    expect(summary.done).toBe(true)
  })

  it("renders a badge instead of throwing", () => {
    const summary = mapRunViewToSummary(runViewFixture({ status: "succeeded" }))

    // The regression: with the cast in place this render threw
    // "Element type is invalid" — `statusIcons["succeeded"]` is `undefined`
    // and React cannot render `undefined` as a component.
    expect(() =>
      render(<StatusBadge status={summary.status} />),
    ).not.toThrow()

    const badge = renderedBadge()
    expect(badge.getAttribute("data-status")).toBe("success")
    expect(badge.textContent).toContain("success")
  })

  it("renders a badge for a cancelled run too", () => {
    const summary = mapRunViewToSummary(runViewFixture({ status: "cancelled" }))

    render(<StatusBadge status={summary.status} />)

    expect(renderedBadge().getAttribute("data-status")).toBe("failed")
  })
})

describe("the duty list's triage sort", () => {
  function summaryFixture(status: string, durationSec: number): RunSummary {
    return mapRunViewToSummary(
      runViewFixture({
        status,
        createdAt: "2026-09-04T10:00:00.000+00:00",
        updatedAt: new Date(
          Date.parse("2026-09-04T10:00:00.000+00:00") + durationSec * 1000,
        ).toISOString(),
      }),
    )
  }

  it("gives every wire status a real rank, so the comparator never sees NaN", () => {
    for (const wireWord of [
      "queued",
      "running",
      "waiting",
      "escalated",
      "succeeded",
      "failed",
      "cancelled",
      "something-the-fe-has-never-heard-of",
    ]) {
      expect(TRIAGE_RANK[normalizeRunStatus(wireWord)]).toEqual(
        expect.any(Number),
      )
      expect(Number.isNaN(TRIAGE_RANK[normalizeRunStatus(wireWord)])).toBe(
        false,
      )
    }
  })

  it("sorts a wire-fed list worst-first instead of leaving it in wire order", () => {
    const runs = [
      summaryFixture("succeeded", 10),
      summaryFixture("escalated", 20),
      summaryFixture("running", 30),
    ]

    expect(triageOrder(runs).map((run) => run.status)).toEqual([
      "escalated",
      "running",
      "success",
    ])
  })
})
