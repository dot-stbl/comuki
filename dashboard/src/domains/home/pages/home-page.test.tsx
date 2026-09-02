import { createContext, useContext, type ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { HomePage } from "@/domains/home/pages/home-page"
import type { RunStatus, RunSummary } from "@/domains/runs/model/types"
import { TestSession } from "@/shared/session/test-session"

/**
 * The screen is driven entirely by one query, so the four states it has to
 * render are four values of that query. Mocking it — rather than reaching
 * through the mock store — is what lets loading and error be tested at all:
 * the seeded store has no way to be slow or to fail.
 */
const state = vi.hoisted(() => ({
  current: {
    data: [] as RunSummary[],
    isLoading: false,
    isError: false,
    error: null as Error | null,
    refetch: vi.fn(),
  },
}))

vi.mock("@/domains/runs/api/queries", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@/domains/runs/api/queries")>()
  return { ...actual, useRunsQuery: () => state.current }
})

/* The outcomes band reads the seed through the home query, which — like every
   query in the product — serves mock data only when the environment says so.
   Pinning it here keeps the band on the screen under test rather than at the
   mercy of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true, repoUrl: null } }))

beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
})

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
    "/",
    "/runs",
    "/runs/$runId",
    "/tasks",
    "/queue",
    "/approvals",
    "/cost",
    "/knowledge",
    "/settings",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function run(
  id: string,
  status: RunStatus,
  durationSec = 300,
  projectId = "p_test"
): RunSummary {
  return {
    id,
    projectId,
    app: "billing-api",
    title: `ticket ${id}`,
    status,
    current: "w1",
    model: "worker",
    cost: 0.4,
    tokens: 8000,
    durationSec,
    done: status === "success",
    workItems: [
      {
        id: "w1",
        profile: "implementer",
        label: "переписать обработчик",
        status,
        dependsOn: [],
      },
    ],
  }
}

function mount() {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-home-test">
      <TestSession roles={["approver"]} projectRoles={{ p_test: ["approver"] }}>
        <QueryClientProvider client={new QueryClient()}>
          <SlotContext value={<HomePage />}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const find = (selector: string) => document.querySelector(selector)

/**
 * The screen has rendered once its own `h1` is up; everything below it is
 * synchronous from the same query result. The heading role rather than the
 * text, because the rail carries an "Attention" item too and the screen's
 * title is the one that means the screen has arrived.
 *
 * Handles are read with a selector rather than `findByTestId`, which looks for
 * `data-testid`; this codebase marks its handles `data-test` throughout, and a
 * test is the wrong place to start a second convention.
 */
async function settled() {
  await screen.findByRole("heading", { level: 1, name: "Attention" })
}

async function verdictNode() {
  await settled()
  const node = find("[data-test='attention-verdict']")
  expect(node).not.toBeNull()
  return node as HTMLElement
}

beforeEach(() => {
  state.current = {
    data: [],
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }
})

describe("before the shift has loaded", () => {
  it("draws the shape the verdict will arrive in, and no verdict", async () => {
    state.current.isLoading = true
    mount()

    await settled()
    expect(find("[data-test='home-loading']")).not.toBeNull()
    // The one thing a loading screen must never do here is imply an answer.
    expect(find("[data-test='attention-verdict']")).toBeNull()
  })
})

describe("when the shift cannot be loaded", () => {
  it("says so, and refuses to imply that nothing is owed", async () => {
    state.current.isError = true
    state.current.error = new Error("network is down")
    mount()

    await settled()
    expect(find("[data-test='home-error']")).not.toBeNull()
    expect(screen.getByText(/network is down/)).not.toBeNull()
    // No verdict at all — "nothing needs you" from a failed load would be the
    // single most dangerous sentence this screen could print.
    expect(find("[data-test='attention-verdict']")).toBeNull()
  })

  it("offers the retry the failure implies", async () => {
    state.current.isError = true
    state.current.error = new Error("network is down")
    mount()

    fireEvent.click(await screen.findByRole("button", { name: "Retry" }))
    expect(state.current.refetch).toHaveBeenCalled()
  })
})

describe("when nothing needs a person", () => {
  beforeEach(() => {
    state.current.data = [
      run("r1", "running", 90),
      run("r2", "running", 900),
      run("q1", "queued", 0),
      run("s1", "success", 500),
    ]
  })

  it("says so plainly, in the same slot the figure would have used", async () => {
    mount()

    const verdict = await verdictNode()
    expect(verdict.getAttribute("data-clear")).toBe("true")
    expect(screen.getByText("Nothing needs you")).not.toBeNull()
  })

  it("renders no list rather than an empty one", async () => {
    mount()

    await verdictNode()
    // An empty list and a list that failed to load look identical. There is no
    // list: the verdict is the whole answer.
    expect(find("[data-test='attention-list']")).toBeNull()
  })

  it("keeps the swarm on screen, so a good state cannot read as a dead one", async () => {
    mount()

    await verdictNode()
    // Live figures in the verdict's own line, and the rows behind them below —
    // together they prove the data arrived rather than merely failing quietly.
    expect(
      screen.getByText(/the swarm is moving on its own/)
    ).not.toBeNull()
    const running = [
      ...document.querySelectorAll("[data-test='running-now'] [data-run]"),
    ].map((node) => node.getAttribute("data-run"))
    expect(running).toEqual(["r2", "r1"])
  })

  it("still says nothing needs you when the swarm is empty", async () => {
    state.current.data = []
    mount()

    await screen.findByText("Nothing needs you")
    expect(
      screen.getByText(/The swarm is empty/)
    ).not.toBeNull()
  })
})

describe("when a decision is owed", () => {
  it("leads with the count and the statuses behind it", async () => {
    state.current.data = [
      run("w1", "waiting"),
      run("e1", "escalated"),
      run("f1", "failed"),
      run("run", "running"),
    ]
    mount()

    const verdict = await verdictNode()
    expect(verdict.getAttribute("data-clear")).toBeNull()
    // The worst status present sets the band, and the words name every one.
    expect(verdict.getAttribute("data-status")).toBe("escalated")
    expect(verdict.textContent).toContain("3")
    expect(verdict.textContent).toContain("runs need a decision")
  })

  it("orders the rows worst-first, the way the duty list already means it", async () => {
    state.current.data = [
      run("w-short", "waiting", 30),
      run("w-long", "waiting", 900),
      run("f1", "failed", 60),
      run("e1", "escalated", 10),
    ]
    mount()

    await settled()
    const rows = [
      ...document.querySelectorAll("[data-test='attention-row']"),
    ].map((node) => node.getAttribute("data-run"))
    expect(rows).toEqual(["e1", "f1", "w-long", "w-short"])
  })

  it("caps the list and names what did not fit, without capping the figure", async () => {
    state.current.data = Array.from({ length: 15 }, (_, index) =>
      run(`w${index}`, "waiting", 100 + index)
    )
    mount()

    const verdict = await verdictNode()
    // The number is the truth; the list is a reading of it.
    expect(verdict.textContent).toContain("15")
    expect(
      document.querySelectorAll("[data-test='attention-row']")
    ).toHaveLength(12)
    expect(
      screen.getByText("and 3 more — open live runs")
    ).not.toBeNull()
  })

  it("draws the week of outcomes inside the running-now band", async () => {
    state.current.data = [run("r1", "running", 90)]
    mount()

    await verdictNode()
    // The outcomes query is a real one — unlike the mocked runs query above,
    // it resolves on its own tick, and the band arrives when it has.
    const band = await waitFor(() => {
      const found = find("[data-test='home-outcomes']")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })

    // Seven columns stacked by outcome, today last, and every status word in
    // the legend — hue is never the only channel on this chart.
    const bars = [
      ...document.querySelectorAll("[data-test='home-outcomes'] [data-test='bar-series-bar']"),
    ]
    expect(bars.length).toBeGreaterThanOrEqual(7 * 2)
    expect(
      bars[bars.length - 1].getAttribute("data-key")
    ).toBe("today")
    expect(
      bars.filter((bar) => bar.getAttribute("data-status") === "success").length
    ).toBe(7)

    const legend = band.textContent ?? ""
    expect(legend).toContain("success")
    expect(legend).toContain("failed")
    expect(legend).toContain("escalated")

    // The figure states the reading in words beside the shape.
    expect(legend).toContain("finished today so far")
    expect(legend).toContain("this week")
  })

  it("keeps the outcomes band below the verdict, not above it", async () => {
    state.current.data = [run("w1", "waiting")]
    mount()

    await verdictNode()
    const outcomes = await waitFor(() => {
      const found = find("[data-test='home-outcomes']")
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    const verdict = find("[data-test='attention-verdict']")

    // History never outranks a decision that is owed now.
    expect(
      verdict!.compareDocumentPosition(outcomes) &
        Node.DOCUMENT_POSITION_FOLLOWING
    ).not.toBe(0)
  })
})
