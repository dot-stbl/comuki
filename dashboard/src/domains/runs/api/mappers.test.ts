import { describe, expect, it, vi } from "vitest"

import {
  mapRunArtifactsPageToArtifacts,
  mapRunViewToDetail,
  mapRunViewToSummary,
  mapRunsPageToSummaries,
  toRunSummary,
} from "@/domains/runs/api/mappers"
import { buildProfileFlow } from "@/domains/runs/model/profile-flow"
import { itemDepths } from "@/domains/runs/model/work-items"
import type { RunView } from "@/shared/api/_generated/types/RunView"
import type { RunsPage } from "@/shared/api/_generated/types/RunsPage"
import type { RunArtifactsPage } from "@/shared/api/_generated/types/RunArtifactsPage"
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

/**
 * Wire → domain mappings for the kubb-generated runs surface.
 *
 * The real backend's row (`RunView`) is intentionally sparse — id,
 * projectId, status, two timestamps — so the domain types fill in the
 * remaining shape with honest defaults. The assertions below pin the
 * shape: a domain `RunSummary` is what the screen expects, even when the
 * backend hasn't filled every column yet.
 */

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

describe("mapRunViewToSummary", () => {
  it("carries id, projectId and status through verbatim", () => {
    const summary = mapRunViewToSummary(runViewFixture({ status: "queued" }))

    expect(summary.id).toBe("00000000-0000-0000-0000-000000000001")
    expect(summary.projectId).toBe("00000000-0000-0000-0000-0000000000aa")
    expect(summary.status).toBe("queued")
  })

  it("defaults the unreachable fields to empty/zero — never fabricates content", () => {
    const summary = mapRunViewToSummary(runViewFixture())

    // The wire carries nothing; the screen renders empty strings and zeros
    // cleanly until a detail endpoint lands.
    expect(summary.app).toBe("")
    expect(summary.title).toBe("")
    expect(summary.current).toBe("")
    expect(summary.cost).toBe(0)
    expect(summary.tokens).toBe(0)
    expect(summary.workItems).toEqual([])
  })

  it("marks the run as done only on terminal status", () => {
    const running = mapRunViewToSummary(runViewFixture({ status: "running" }))
    const queued = mapRunViewToSummary(runViewFixture({ status: "queued" }))
    const succeeded = mapRunViewToSummary(
      runViewFixture({ status: "succeeded" }),
    )
    const failed = mapRunViewToSummary(runViewFixture({ status: "failed" }))
    const cancelled = mapRunViewToSummary(
      runViewFixture({ status: "cancelled" }),
    )

    expect(running.done).toBe(false)
    expect(queued.done).toBe(false)
    expect(succeeded.done).toBe(true)
    expect(failed.done).toBe(true)
    expect(cancelled.done).toBe(true)
  })

  it("computes duration as the rounded (updatedAt - createdAt) delta in seconds", () => {
    const createdAt = "2026-09-04T10:00:00.000+00:00"
    const updatedAt = "2026-09-04T10:07:25.499+00:00"

    const summary = mapRunViewToSummary(
      runViewFixture({ createdAt, updatedAt }),
    )

    // 7 minutes 25 seconds → rounds to 445 seconds (we keep the integer, not
    // expose the millisecond precision the wire actually carries).
    expect(summary.durationSec).toBe(445)
  })

  it("never reports a negative duration when the timestamps cross over", () => {
    const summary = mapRunViewToSummary(
      runViewFixture({
        createdAt: "2026-09-04T10:01:00.000+00:00",
        updatedAt: "2026-09-04T10:00:00.000+00:00",
      }),
    )

    expect(summary.durationSec).toBe(0)
  })
})

describe("mapRunsPageToSummaries", () => {
  it("drops the wire's paging envelope and maps each row", () => {
    const page: RunsPage = {
      items: [
        runViewFixture({ id: "id-1", status: "running" }),
        runViewFixture({ id: "id-2", status: "queued" }),
      ],
      page: 1,
      pageSize: 100,
      total: 2,
    }

    const summaries = mapRunsPageToSummaries(page)

    expect(summaries).toHaveLength(2)
    expect(summaries[0]?.id).toBe("id-1")
    expect(summaries[0]?.status).toBe("running")
    expect(summaries[1]?.id).toBe("id-2")
    expect(summaries[1]?.status).toBe("queued")
  })

  it("returns an empty array for a page with no rows", () => {
    const page: RunsPage = {
      items: [],
      page: 1,
      pageSize: 100,
      total: 0,
    }

    expect(mapRunsPageToSummaries(page)).toEqual([])
  })
})

describe("mapRunViewToDetail", () => {
  it("carries the summary fields through", () => {
    const detail = mapRunViewToDetail(runViewFixture({ status: "running" }))

    expect(detail.id).toBe("00000000-0000-0000-0000-000000000001")
    expect(detail.status).toBe("running")
    expect(detail.workItems).toEqual([])
  })

  it("leaves the detail-only fields empty — the wire doesn't carry them yet", () => {
    const detail = mapRunViewToDetail(runViewFixture())

    expect(detail.brief).toBe("")
    expect(detail.rules).toEqual([])
    expect(detail.revision).toEqual({ rules: "", sdk: "" })
    expect(detail.events).toEqual([])
  })
})

describe("mapRunArtifactsPageToArtifacts", () => {
  function pageFixture(
    overrides: Partial<RunArtifactsPage> = {},
  ): RunArtifactsPage {
    return {
      projectId: "00000000-0000-0000-0000-0000000000aa",
      runId: "00000000-0000-0000-0000-000000000001",
      items: [],
      ...overrides,
    }
  }

  it("returns an empty page when the run has not been packaged yet", () => {
    const page = pageFixture()

    const artifacts = mapRunArtifactsPageToArtifacts(page)

    expect(artifacts.projectId).toBe("00000000-0000-0000-0000-0000000000aa")
    expect(artifacts.runId).toBe("00000000-0000-0000-0000-000000000001")
    expect(artifacts.items).toEqual([])
  })

  it("normalizes each entry to a URL and handles the wire's loose size type", () => {
    const page = pageFixture({
      items: [
        {
          name: "brief.json",
          uri: "https://minio.example.com/run-1/brief.json?signature=abc",
          size: 1024,
          contentType: "application/json",
        },
        {
          name: "logs.txt",
          uri: "https://minio.example.com/run-1/logs.txt",
          size: "2048",
          contentType: "text/plain",
        },
      ],
    })

    const artifacts = mapRunArtifactsPageToArtifacts(page)

    expect(artifacts.items).toHaveLength(2)
    expect(artifacts.items[0]?.name).toBe("brief.json")
    expect(artifacts.items[0]?.uri).toBeInstanceOf(URL)
    expect(artifacts.items[0]?.uri.toString()).toBe(
      "https://minio.example.com/run-1/brief.json?signature=abc",
    )
    expect(artifacts.items[0]?.size).toBe(1024)
    expect(artifacts.items[0]?.contentType).toBe("application/json")
    // string-encoded size from kubb's loose int64 typing → coerced to number.
    expect(artifacts.items[1]?.size).toBe(2048)
  })

  it("drops entries with malformed URIs rather than failing the page", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined)

    const page = pageFixture({
      items: [
        {
          name: "good.json",
          uri: "https://minio.example.com/run-1/good.json",
          size: 10,
          contentType: "application/json",
        },
        {
          name: "bad.json",
          uri: "not a url",
          size: 10,
          contentType: "application/json",
        },
      ],
    })

    const artifacts = mapRunArtifactsPageToArtifacts(page)

    expect(artifacts.items).toHaveLength(1)
    expect(artifacts.items[0]?.name).toBe("good.json")
    expect(warn).toHaveBeenCalled()

    warn.mockRestore()
  })
})
