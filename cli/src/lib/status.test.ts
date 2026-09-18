import { describe, expect, it } from "bun:test"
import { ComukiApiError } from "./client"
import type {
  ComputeSnapshotView,
  HealthView,
  KnowledgeDocumentsPageView,
  ProjectView,
  RunsPageView,
} from "./client"
import {
  describeStatusError,
  fetchStatusSnapshot,
  renderStatusLine,
  renderStatusPanel,
  statusLines,
  type StatusSnapshot,
} from "./status"
import { stripAnsi } from "../theme"

function fulfilled<T>(value: T): PromiseFulfilledResult<T> {
  return { status: "fulfilled", value }
}

function rejected(reason: unknown): PromiseRejectedResult {
  return { status: "rejected", reason }
}

const health: HealthView = { status: "ok" }

const compute: ComputeSnapshotView = {
  provider: "docker",
  defaults: { workerImage: "comuki/worker", minIdle: 0, maxConcurrent: 4 },
  pools: [
    {
      projectId: "p1",
      profileKey: "implement",
      queued: 2,
      running: 1,
      minIdle: 0,
      maxConcurrent: 4,
    },
  ],
}

const projects: readonly ProjectView[] = [
  {
    id: "p1",
    name: "Nova",
    slug: "nova",
    description: null,
    archived: false,
  },
]

const knowledge: KnowledgeDocumentsPageView = {
  items: [
    {
      id: "d1",
      projectId: "p1",
      title: "notes",
      source: "upload",
      sourceRef: "upload:notes.md",
      mimeType: "text/markdown",
      chunkCount: 7,
      tokenCount: 120,
      createdAt: "2026-09-18T00:00:00Z",
    },
  ],
  page: 1,
  pageSize: 100,
  total: 1,
}

const runs: RunsPageView = {
  items: [
    {
      id: "r1",
      projectId: "p1",
      status: "running",
      createdAt: "2026-09-18T00:00:00Z",
      updatedAt: "2026-09-18T00:01:00Z",
    },
    {
      id: "r2",
      projectId: "p1",
      status: "queued",
      createdAt: "2026-09-18T00:00:00Z",
      updatedAt: "2026-09-18T00:00:30Z",
    },
  ],
  page: 1,
  pageSize: 100,
  total: 2,
}

function snapshot(overrides: Partial<StatusSnapshot> = {}): StatusSnapshot {
  return {
    health: fulfilled(health),
    compute: fulfilled(compute),
    projects: fulfilled(projects),
    knowledge: fulfilled(knowledge),
    runs: fulfilled(runs),
    ...overrides,
  }
}

describe("statusLines", () => {
  it("renders every source when all succeed", () => {
    const lines = statusLines(snapshot()).map((line) =>
      line.ok ? stripAnsi(line.text) : `${line.label}: ${line.reason}`
    )
    expect(lines[0]).toContain("health:")
    expect(lines[0]).toContain("ok")
    expect(lines[1]).toContain("provider:")
    expect(lines[1]).toContain("docker")
    expect(lines[1]).toContain("2 queued")
    expect(lines[1]).toContain("1 running")
    expect(lines[2]).toContain("projects:")
    expect(lines[2]).toContain("1 active")
    expect(lines[3]).toContain("knowledge:")
    expect(lines[3]).toContain("1 documents")
    expect(lines[3]).toContain("7 chunks")
    expect(lines[4]).toContain("runs:")
    expect(lines[4]).toContain("2 total")
    expect(lines[4]).toContain("1 queued")
    expect(lines[4]).toContain("1 running")
  })

  it("a failed source prints its reason and does not blank the rest", () => {
    const lines = statusLines(
      snapshot({
        compute: rejected(
          new ComukiApiError(403, "permission.denied", "compute:read required")
        ),
      })
    )
    expect(lines[1]).toEqual({
      ok: false,
      label: "provider",
      reason: "HTTP 403 (permission.denied): compute:read required",
    })
    expect(lines[0]?.ok).toBe(true)
    expect(lines[2]?.ok).toBe(true)
  })

  it("knowledge notes when the first page is not the whole library", () => {
    const line = statusLines(
      snapshot({
        knowledge: fulfilled({ ...knowledge, total: 140 }),
      })
    )[3]
    expect(line?.ok).toBe(true)
    if (line?.ok) {
      expect(stripAnsi(line.text)).toContain("+139 more, first page only")
    }
  })
})

describe("renderStatusPanel", () => {
  it("leads with a * status header and one row per source", () => {
    const lines = renderStatusPanel(snapshot()).map(stripAnsi)
    expect(lines[0]).toContain("status")
    expect(lines).toHaveLength(6)
    expect(lines[1]).toContain("health:")
  })

  it("failed rows stay dim `label: reason`", () => {
    const line = stripAnsi(
      renderStatusLine({
        ok: false,
        label: "runs",
        reason: "server unreachable",
      })
    )
    expect(line).toBe("runs: server unreachable")
  })
})

describe("describeStatusError", () => {
  it("keeps HTTP code + detail from ComukiApiError", () => {
    expect(
      describeStatusError(
        new ComukiApiError(502, "provider.network_error", "upstream unavailable")
      )
    ).toBe("HTTP 502 (provider.network_error): upstream unavailable")
  })

  it("collapses a refused connection to 'server unreachable'", () => {
    expect(describeStatusError(new Error("fetch failed"))).toBe(
      "server unreachable"
    )
    expect(describeStatusError(new Error("ECONNREFUSED"))).toBe(
      "server unreachable"
    )
  })
})

describe("fetchStatusSnapshot", () => {
  it("settles each source independently — one rejection does not abort the rest", async () => {
    const result = await fetchStatusSnapshot({
      health: async () => ({ status: "ok" }),
      compute: async () => {
        throw new ComukiApiError(403, "permission.denied", "no")
      },
      projects: async () => [],
      knowledgeDocuments: async () => ({
        items: [],
        page: 1,
        pageSize: 100,
        total: 0,
      }),
      runs: async () => ({ items: [], page: 1, pageSize: 100, total: 0 }),
    })
    expect(result.health.status).toBe("fulfilled")
    expect(result.compute.status).toBe("rejected")
    expect(result.projects.status).toBe("fulfilled")
  })
})
