import { describe, expect, it } from "bun:test"
import {
  mergeRunsFeedRefresh,
  planPanelLines,
  projectListingLines,
  projectSwitchedLine,
  renderRunsFeedPanel,
  renderWorkersPanel,
  resolveProject,
  runStatusColor,
  runsFeedRows,
  workerStatusColor,
  workerStatusWord,
  type RunsFeedPanel,
} from "./runsfeed"
import type {
  BackgroundWorkerView,
  ChatMessageView,
  MessagePart,
  ProjectView,
  RunsPageView,
} from "./client"
import { colors, stripAnsi, symbols } from "../theme"
import type { ChatBlock } from "./sessions"

function runPage(items: RunsPageView["items"], total = items.length): RunsPageView {
  return { items, page: 1, pageSize: items.length, total }
}

function panel(overrides: Partial<RunsFeedPanel> = {}): RunsFeedPanel {
  return {
    rows: [],
    total: 0,
    fetchedAt: 0,
    refreshError: null,
    ...overrides,
  }
}

function project(id: string, slug: string, name = slug): ProjectView {
  return { id, name, slug, description: null, archived: false }
}

describe("runsFeedRows", () => {
  it("maps the page to rows with short ids, resolved slugs and raw status", () => {
    const rows = runsFeedRows(
      runPage([
        {
          id: "3f9c2a1b-1111-2222-3333-444444444444",
          projectId: "p1",
          status: "running",
          createdAt: "2026-09-18T00:00:00Z",
          updatedAt: "2026-09-18T00:01:00Z",
        },
      ]),
      new Map([["p1", "nova"]])
    )

    expect(rows).toHaveLength(1)
    expect(rows[0]?.id).toBe("3f9c2a1b-1111-2222-3333-444444444444")
    expect(rows[0]?.project).toBe("nova")
    expect(rows[0]?.status).toBe("running")
    expect(rows[0]?.updatedAt).toBe("2026-09-18T00:01:00Z")
  })

  it("falls back to the short project id when the name is unresolvable", () => {
    const rows = runsFeedRows(
      runPage([
        {
          id: "r1",
          projectId: "abcdef01-2222-3333-4444-555555555555",
          status: "queued",
          createdAt: "2026-09-18T00:00:00Z",
          updatedAt: "2026-09-18T00:00:00Z",
        },
      ]),
      new Map()
    )

    expect(rows[0]?.project).toBe("abcdef01")
  })
})

describe("runStatusColor", () => {
  it("assigns the dichromat deck band per status family", () => {
    expect(runStatusColor("running")).toBe(colors.accent)
    expect(runStatusColor("succeeded")).toBe(colors.ok)
    expect(runStatusColor("failed")).toBe(colors.error)
    expect(runStatusColor("waiting")).toBe(colors.waiting)
    expect(runStatusColor("queued")).toBe(colors.faint)
    expect(runStatusColor("something-else")).toBe(colors.muted)
  })
})

describe("renderRunsFeedPanel", () => {
  const now = new Date("2026-09-18T12:01:00Z")

  it("renders the header, column line, status bands and the refresh footer", () => {
    const lines = renderRunsFeedPanel(
      panel({
        rows: [
          {
            id: "3f9c2a1b-4d5e",
            project: "nova",
            status: "running",
            updatedAt: "2026-09-18T12:00:00Z",
          },
          {
            id: "99887766-5544",
            project: "orion",
            status: "failed",
            updatedAt: "2026-09-18T11:30:00Z",
          },
        ],
        total: 12,
        fetchedAt: now.getTime() - 5_000,
      }),
      now
    )
    const plain = lines.map(stripAnsi)

    expect(plain[0]).toContain("runs · 2 of 12 · 5s ago")
    expect(plain[1]).toContain("id")
    expect(plain[1]).toContain("status")
    expect(plain[1]).toContain("project")
    expect(plain[1]).toContain("age")
    expect(plain[2]).toContain("3f9c2a1b-4d5e")
    expect(plain[2]).toContain("running")
    expect(plain[2]).toContain("nova")
    expect(plain[2]).toContain("1m")
    expect(plain[3]).toContain("failed")
    // Colour never carries status alone: the word rides beside its band.
    expect(lines[2]).toContain(colors.accent)
    expect(lines[3]).toContain(colors.error)
    const footer = plain[plain.length - 1] ?? ""
    expect(footer).toContain("auto-refresh 60s")
    expect(footer).toContain("/runs refreshes now")
  })

  it("renders the empty state footer when no runs exist", () => {
    const plain = renderRunsFeedPanel(panel({ fetchedAt: 0 }), now).map(stripAnsi)

    expect(plain).toContain("  no runs yet")
  })

  it("surfaces the last refresh failure without dropping the rows", () => {
    const lines = renderRunsFeedPanel(
      panel({
        rows: [
          {
            id: "r1",
            project: "nova",
            status: "running",
            updatedAt: "2026-09-18T12:00:00Z",
          },
        ],
        total: 1,
        fetchedAt: now.getTime() - 30_000,
        refreshError: "server unreachable",
      }),
      now
    )
    const plain = lines.map(stripAnsi)

    expect(plain.some((line) => line.includes("3f9c") || line.includes("r1"))).toBe(
      true
    )
    expect(plain[plain.length - 1]).toContain("server unreachable")
    expect(lines[lines.length - 1]).toContain(colors.error)
  })

  it("renders a bare error panel when the first fetch failed", () => {
    const plain = renderRunsFeedPanel(
      panel({ refreshError: "HTTP 401: denied" }),
      now
    ).map(stripAnsi)

    expect(plain).toHaveLength(2)
    expect(plain[1]).toContain(`${symbols.cross} HTTP 401: denied`)
  })
})

describe("mergeRunsFeedRefresh", () => {
  it("keeps the previous rows when the new poll failed", () => {
    const previous = panel({
      rows: [
        {
          id: "r1",
          project: "nova",
          status: "running",
          updatedAt: "2026-09-18T12:00:00Z",
        },
      ],
      total: 1,
      fetchedAt: 100,
    })
    const merged = mergeRunsFeedRefresh(previous, panel({ refreshError: "boom" }))

    expect(merged.rows).toBe(previous.rows)
    expect(merged.refreshError).toBe("boom")
  })

  it("returns the fresh panel on success and when there is nothing to rescue", () => {
    const fresh = panel({ total: 7 })
    expect(mergeRunsFeedRefresh(panel({}), fresh)).toBe(fresh)
    expect(mergeRunsFeedRefresh(undefined, fresh)).toBe(fresh)
    expect(
      mergeRunsFeedRefresh(panel({ refreshError: "old" }), fresh)
    ).toBe(fresh)
  })
})

describe("planPanelLines", () => {
  function planMessage(nodes: MessagePart[]): ChatBlock {
    const message: ChatMessageView = {
      id: "m1",
      role: "assistant",
      content: "",
      toolName: null,
      parts: nodes,
      meta: null,
      createdAt: "2026-09-18T00:00:00Z",
    }
    return { kind: "message", key: "k1", message }
  }

  it("renders the pending plan with the awaiting-approval header", () => {
    const lines = planPanelLines(
      [],
      {
        nodes: [
          { key: "n1", profileKey: "implement", brief: "do it", dependsOn: [] },
        ],
      }
    )
    const plain = lines.map(stripAnsi)

    expect(plain[0]).toContain("plan · 1 шаг · awaiting approval")
    expect(plain[1]).toContain("implement")
    expect(plain[1]).toContain("do it")
  })

  it("falls back to the newest plan part carried by the transcript", () => {
    const planPart: MessagePart = {
      kind: "plan",
      nodes: [
        {
          key: "n1",
          profileKey: "review",
          brief: "check it",
          dependsOn: ["n0"],
        },
      ],
      edges: [],
    }
    const lines = planPanelLines(
      [
        planMessage([{ kind: "text", markdown: "no plan here" }]),
        planMessage([planPart]),
      ],
      null
    )
    const plain = lines.map(stripAnsi)

    expect(plain[0]).toContain("plan · 1 шаг")
    expect(plain[0]).not.toContain("awaiting approval")
    expect(plain[1]).toContain("review")
    expect(plain[1]).toContain("<- n0")
  })

  it("prefers the pending plan over the transcript part", () => {
    const lines = planPanelLines(
      [
        planMessage([
          {
            kind: "plan",
            nodes: [
              { key: "t1", profileKey: "stale", brief: "old", dependsOn: [] },
            ],
            edges: [],
          },
        ]),
      ],
      {
        nodes: [
          { key: "p1", profileKey: "live", brief: "fresh", dependsOn: [] },
        ],
      }
    )

    expect(lines.map(stripAnsi).join("\n")).toContain("fresh")
    expect(lines.map(stripAnsi).join("\n")).not.toContain("stale")
  })

  it("shows the dim no-plan notice when neither source has a plan", () => {
    const lines = planPanelLines([], null)

    expect(lines).toHaveLength(1)
    expect(stripAnsi(lines[0] ?? "")).toBe("  no plan in this session")
  })
})

describe("resolveProject", () => {
  const projects = [
    project("11111111-1111-1111-1111-111111111111", "nova", "Nova Platform"),
    project("22222222-2222-2222-2222-222222222222", "orion", "Orion Migration"),
  ]

  it("resolves by id, slug or name, case-insensitively", () => {
    expect(resolveProject("11111111-1111-1111-1111-111111111111", projects)?.slug).toBe("nova")
    expect(resolveProject("NOVA", projects)?.slug).toBe("nova")
    expect(resolveProject("orion migration", projects)?.slug).toBe("orion")
  })

  it("returns undefined on an empty query or a miss", () => {
    expect(resolveProject("", projects)).toBeUndefined()
    expect(resolveProject("  ", projects)).toBeUndefined()
    expect(resolveProject("ghost", projects)).toBeUndefined()
  })
})

describe("projectListingLines", () => {
  it("lists the current context and every visible project", () => {
    const plain = projectListingLines("nova", [
      project("p1", "nova", "Nova Platform"),
      project("p2", "orion", "Orion Migration"),
    ]).map(stripAnsi)

    expect(plain[0]).toContain("project · current: nova")
    expect(plain[1]).toContain("nova — Nova Platform")
    expect(plain[2]).toContain("orion — Orion Migration")
  })

  it("shows none for a null context and a note when nothing is visible", () => {
    const plain = projectListingLines(null, []).map(stripAnsi)

    expect(plain[0]).toContain("current: none")
    expect(plain[1]).toContain("no projects visible")
  })
})

describe("projectSwitchedLine", () => {
  it("renders the dim context-switch notice", () => {
    const line = projectSwitchedLine("orion")

    expect(stripAnsi(line)).toBe(`  ${symbols.event} project ${symbols.arrow} orion`)
    expect(line).toContain(colors.faint)
  })
})

describe("runsFeedRows hyperlink targets", () => {
  it("derives {dashboardUrl}/runs/{id} per row, trailing slash tolerated", () => {
    const rows = runsFeedRows(
      runPage([
        {
          id: "3f9c2a1b-1111-2222-3333-444444444444",
          projectId: "p1",
          status: "running",
          createdAt: "2026-09-18T00:00:00Z",
          updatedAt: "2026-09-18T00:01:00Z",
        },
      ]),
      new Map(),
      "http://h:17173/"
    )

    expect(rows[0]?.url).toBe(
      "http://h:17173/runs/3f9c2a1b-1111-2222-3333-444444444444"
    )
  })

  it("omits the url without a dashboard base — ids render as before", () => {
    const rows = runsFeedRows(
      runPage([
        {
          id: "r1",
          projectId: "p1",
          status: "running",
          createdAt: "2026-09-18T00:00:00Z",
          updatedAt: "2026-09-18T00:01:00Z",
        },
      ]),
      new Map()
    )

    expect(rows[0]?.url).toBeUndefined()
  })
})

describe("renderRunsFeedPanel hyperlinks", () => {
  const now = new Date("2026-09-18T12:01:00Z")

  it("wraps the id cell in OSC 8 when the row carries a url", () => {
    const lines = renderRunsFeedPanel(
      panel({
        rows: [
          {
            id: "3f9c2a1b-4d5e",
            project: "nova",
            status: "running",
            updatedAt: "2026-09-18T12:00:00Z",
            url: "http://h:17173/runs/3f9c2a1b-4d5e",
          },
        ],
        fetchedAt: now.getTime(),
      }),
      now
    )

    const row = lines[2] ?? ""
    expect(row).toContain(
      "\x1b]8;;http://h:17173/runs/3f9c2a1b-4d5e\x1b\\"
    )
    // The label stays visible and column alignment survives the
    // wrapper bytes (stripAnsi drops OSC alongside SGR).
    const plain = stripAnsi(row)
    expect(plain).toContain("3f9c2a1b-4d5e")
    expect(plain.indexOf("running")).toBeGreaterThan(
      plain.indexOf("3f9c2a1b-4d5e")
    )
  })

  it("renders the bare painted id when no url is set", () => {
    const lines = renderRunsFeedPanel(
      panel({
        rows: [
          {
            id: "3f9c2a1b-4d5e",
            project: "nova",
            status: "running",
            updatedAt: "2026-09-18T12:00:00Z",
          },
        ],
        fetchedAt: now.getTime(),
      }),
      now
    )

    expect(lines[2]).not.toContain("\x1b]8;")
  })
})

describe("workerStatusWord / workerStatusColor", () => {
  function worker(
    overrides: Partial<BackgroundWorkerView>
  ): BackgroundWorkerView {
    return {
      name: "memory-sweep",
      lastRunAt: "2026-09-18T11:59:00Z",
      nextRunAt: "2026-09-18T12:01:00Z",
      lastResult: null,
      consecutiveFailures: 0,
      isHealthy: true,
      ...overrides,
    }
  }

  it("degrades on consecutive failures even while scheduled", () => {
    const degraded = worker({
      consecutiveFailures: 3,
      isHealthy: false,
      nextRunAt: "2026-09-18T12:05:00Z",
    })
    expect(workerStatusWord(degraded)).toBe("degraded")
    expect(workerStatusColor("degraded")).toBe(colors.waiting)
  })

  it("reads running while a next cycle is scheduled", () => {
    expect(workerStatusWord(worker({}))).toBe("running")
    expect(workerStatusColor("running")).toBe(colors.ok)
  })

  it("reads stopped for a finished startup worker (no next run, healthy)", () => {
    const stopped = worker({ nextRunAt: null })
    expect(workerStatusWord(stopped)).toBe("stopped")
    expect(workerStatusColor("stopped")).toBe(colors.dim)
  })
})

describe("renderWorkersPanel", () => {
  const now = new Date("2026-09-18T12:01:00Z")

  function worker(
    name: string,
    overrides: Partial<BackgroundWorkerView> = {}
  ): BackgroundWorkerView {
    return {
      name,
      lastRunAt: "2026-09-18T12:00:30Z",
      nextRunAt: "2026-09-18T12:01:30Z",
      lastResult: { success: true, detail: null, data: null },
      consecutiveFailures: 0,
      isHealthy: true,
      ...overrides,
    }
  }

  it("renders the header, column line, one row per worker and the one-shot footer", () => {
    const lines = renderWorkersPanel(
      [worker("lease-reaper"), worker("memory-sweep")],
      undefined,
      now
    )
    const plain = lines.map(stripAnsi)

    expect(plain[0]).toContain("workers · 2")
    expect(plain[1]).toContain("name")
    expect(plain[1]).toContain("status")
    expect(plain[1]).toContain("last-run")
    expect(plain[2]).toContain("lease-reaper")
    expect(plain[2]).toContain("running")
    expect(plain[2]).toContain("30s")
    expect(plain[3]).toContain("memory-sweep")
    expect(plain[plain.length - 1]).toContain("one-shot")
    expect(plain[plain.length - 1]).toContain("/workers refreshes")
  })

  it("bands statuses in the dichromat tones: degraded waiting-yellow, stopped dim", () => {
    const lines = renderWorkersPanel(
      [
        worker("memory-sweep"),
        worker("lease-reaper", {
          consecutiveFailures: 2,
          isHealthy: false,
        }),
        worker("oidc-sweep", { nextRunAt: null }),
      ],
      undefined,
      now
    )

    expect(lines[2]).toContain(colors.ok)
    expect(lines[2]).toContain("running")
    expect(lines[3]).toContain(colors.waiting)
    expect(lines[3]).toContain("degraded")
    expect(lines[4]).toContain("stopped")
  })

  it("shows never for a worker without a first run", () => {
    const plain = renderWorkersPanel(
      [worker("oidc-sweep", { lastRunAt: null, nextRunAt: null })],
      undefined,
      now
    ).map(stripAnsi)

    expect(plain[2]).toContain("never")
  })

  it("renders the empty state when the registry has no workers", () => {
    const plain = renderWorkersPanel([], undefined, now).map(stripAnsi)

    expect(plain).toHaveLength(2)
    expect(plain[0]).toContain("workers · 0")
    expect(plain[1]).toContain("no background workers")
  })

  it("links worker names to the dashboard root via OSC 8 when a url is supplied", () => {
    const lines = renderWorkersPanel(
      [worker("memory-sweep")],
      "http://h:17173",
      now
    )

    expect(lines[2]).toContain("\x1b]8;;http://h:17173\x1b\\")
    // Alignment survives: stripAnsi drops the wrapper, name stays put.
    expect(stripAnsi(lines[2] ?? "")).toContain("memory-sweep")
  })
})
