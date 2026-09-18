import { describe, expect, it } from "bun:test"
import {
  mapRunJson,
  mapRunsPageJson,
  mapStatusJson,
  mapWhoamiJson,
} from "./jsonout"
import type {
  ComputeSnapshotView,
  KnowledgeDocumentsPageView,
  MeView,
  ProjectView,
  RunsPageView,
  RunView,
} from "./client"

const run = (id: string, status: string, projectId = "p1"): RunView => ({
  id,
  projectId,
  status,
  createdAt: "2026-09-18T10:00:00Z",
  updatedAt: "2026-09-18T10:01:00Z",
})

describe("mapStatusJson", () => {
  it("projects a full snapshot into identity + counts", () => {
    const compute: ComputeSnapshotView = {
      provider: "docker",
      defaults: { workerImage: "comuki/worker", minIdle: 0, maxConcurrent: 4 },
      pools: [
        {
          projectId: "p1",
          profileKey: "coder",
          queued: 2,
          running: 1,
          minIdle: 0,
          maxConcurrent: 4,
        },
        {
          projectId: "p2",
          profileKey: "reviewer",
          queued: 1,
          running: 3,
          minIdle: 0,
          maxConcurrent: 4,
        },
      ],
    }
    const projects: readonly ProjectView[] = [
      { id: "p1", name: "Nova", slug: "nova", description: null, archived: false },
      { id: "p2", name: "Orion", slug: "orion", description: null, archived: false },
    ]
    const knowledge: KnowledgeDocumentsPageView = {
      items: [
        {
          id: "d1",
          projectId: null,
          title: "a",
          source: "file",
          sourceRef: "a.md",
          mimeType: "text/markdown",
          chunkCount: 4,
          tokenCount: 100,
          createdAt: "2026-09-18T00:00:00Z",
        },
        {
          id: "d2",
          projectId: null,
          title: "b",
          source: "file",
          sourceRef: "b.md",
          mimeType: "text/markdown",
          chunkCount: 6,
          tokenCount: 200,
          createdAt: "2026-09-18T00:00:00Z",
        },
      ],
      page: 1,
      pageSize: 100,
      total: 2,
    }
    const runs: RunsPageView = {
      items: [run("r1", "queued"), run("r2", "running"), run("r3", "queued")],
      page: 1,
      pageSize: 100,
      total: 3,
    }

    expect(
      mapStatusJson({
        who: { kind: "user", label: "brad" },
        compute,
        projects,
        knowledge,
        runs,
      })
    ).toEqual({
      identity: { kind: "user", label: "brad" },
      compute: { provider: "docker", queued: 3, running: 4 },
      projects: { count: 2 },
      knowledge: { documents: 2, chunks: 10 },
      runs: { total: 3, byStatus: { queued: 2, running: 1 } },
      errors: {},
    })
  })

  it("nulls missing sources and carries per-source errors", () => {
    expect(
      mapStatusJson({
        who: { kind: "anonymous", label: "offline" },
        errors: { compute: "server unreachable" },
      })
    ).toEqual({
      identity: { kind: "anonymous", label: "offline" },
      compute: null,
      projects: null,
      knowledge: null,
      runs: null,
      errors: { compute: "server unreachable" },
    })
  })
})

describe("mapRunsPageJson", () => {
  it("maps each run and resolves the project slug when known", () => {
    const page: RunsPageView = {
      items: [run("r1", "running", "p1"), run("r2", "failed", "p-unknown")],
      page: 2,
      pageSize: 20,
      total: 40,
    }
    const mapped = mapRunsPageJson(page, new Map([["p1", "nova"]]))
    expect(mapped.page).toBe(2)
    expect(mapped.pageSize).toBe(20)
    expect(mapped.total).toBe(40)
    expect(mapped.items[0]).toEqual({
      id: "r1",
      status: "running",
      projectId: "p1",
      project: "nova",
      createdAt: "2026-09-18T10:00:00Z",
      updatedAt: "2026-09-18T10:01:00Z",
    })
    expect(mapped.items[1]?.project).toBeNull()
    expect(mapped.items[1]?.projectId).toBe("p-unknown")
  })

  it("mapRunJson defaults the project slug to null", () => {
    expect(mapRunJson(run("r1", "queued")).project).toBeNull()
  })
})

describe("mapWhoamiJson", () => {
  it("merges the whoami label with the me payload", () => {
    const me: MeView = {
      userId: "u1",
      subjectType: "user",
      subjectId: "s1",
      email: "a@b.c",
      displayName: "Ada",
      roles: ["admin"],
      permissions: ["chat:write"],
    }
    expect(mapWhoamiJson({ kind: "user", label: "Ada" }, me)).toEqual({
      kind: "user",
      label: "Ada",
      userId: "u1",
      subjectType: "user",
      subjectId: "s1",
      email: "a@b.c",
      displayName: "Ada",
      roles: ["admin"],
      permissions: ["chat:write"],
    })
  })

  it("fills empty identity fields when me is missing", () => {
    expect(mapWhoamiJson({ kind: "anonymous", label: "offline" }, null)).toEqual({
      kind: "anonymous",
      label: "offline",
      userId: null,
      subjectType: null,
      subjectId: null,
      email: null,
      displayName: null,
      roles: [],
      permissions: [],
    })
  })
})
