import { createContext, useContext } from "react"
import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { TestSession } from "@/shared/session/test-session"

import { RunDetailPage } from "./run-detail-page"

/* One run, in full — and the four answers it can give before it has one.
 *
 * The page was the last of the five detail screens to take its id as a prop and
 * the last to tell "this id is not a run" apart from "the request failed", and
 * both of those are what this file is for: the id arrives as a value, so the
 * screen can be mounted here without the generated route tree, and each of the
 * four readings is reachable from the outside.
 *
 * Three of the four come out of the real mock seed with no help. A load
 * failure is not reachable through a store that cannot fail, so the seed lookup
 * is the one thing stubbed, and only while a test asks for it.
 *
 * The loading frame is the one reading with no test here, and it cannot have
 * one: the router settles its initial load and the mock query resolves inside
 * the same `act` flush that renders them, so there is no frame in which the
 * skeleton exists to be found. Recorded here rather than papered over with an
 * assertion that would pass just as well on a page with no skeleton in it at
 * all — `queue/pages/worker-detail-page.test.tsx` reached the same wall and
 * wrote the same note. */

vi.mock("@/shared/config/env", () => ({
  env: { useMock: true, apiBaseUrl: "" },
}))

/** Flipped per test: every other case below needs the seed working normally. */
let seedFails = false

vi.mock("@/shared/api/mock", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/api/mock")>()
  return {
    ...actual,
    findSeedRun: (runId: string) => {
      if (seedFails) {
        throw new Error("the swarm is unreachable")
      }
      return actual.findSeedRun(runId)
    },
  }
})

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws `No layout data found for index 0` when every element it measures is
   zero — which, in jsdom, they all are. This screen is two pane groups deep
   (the shell's rail, then the graph over the inspector), so the stub is what
   lets it mount at all. It costs nothing that could have been had: jsdom
   computes no layout, so nothing below was ever going to check one. The height
   chain is hand-traced at the top of `run-detail-page.module.css`. */
vi.mock("react-resizable-panels", () => ({
  Group: ({
    children,
    className,
  }: {
    children: ReactNode
    className?: string
  }) => (
    <div className={className} data-test="split-pane">
      {children}
    </div>
  ),
  Panel: ({
    children,
    className,
    id,
  }: {
    children: ReactNode
    className?: string
    id?: string
  }) => (
    <div className={className} data-panel={id}>
      {children}
    </div>
  ),
  Separator: ({ className }: { className?: string }) => (
    <div role="separator" className={className} data-test="split-separator" />
  ),
}))

beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: 480,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1400,
  })

  /* `SplitPane` persists a divider position, and in jsdom the layout it saved
     was measured against nothing. Nothing is ever read back. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  seedFails = false
})

/* The rail links to every product screen, so a memory router that does not
   know those paths cannot render the shell at all. The screen itself rides the
   root route through a slot rather than sitting at an address of its own —
   `runs-page.test.tsx` is the worked harness, and the arrangement says the
   thing this file is about out loud: the route tree below has never heard of
   `/runs/$runId`, and the page renders anyway, because the only thing it ever
   wanted from the router was one string. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
    "/",
    "/tasks",
    "/runs",
    "/queue",
    "/approvals",
    "/cost",
    "/knowledge",
    "/verify",
    "/sources",
    "/settings",
    "/projects",
    "/identity",
    "/compute",
    "/models",
    "/observability",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function mount(_runId: string) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["platform-admin"]}>
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          <SlotContext value={<RunDetailPage />}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const at = (test: string) => document.querySelector(`[data-test="${test}"]`)

/** The first hand-written seed run: eight items, a branch, one of them live. */
const SEEDED_RUN = "8f3c2a91"

describe("what the screen answers before the run is on it", () => {
  it("draws the run once it arrives", async () => {
    mount(SEEDED_RUN)

    await waitFor(() => expect(at("run-graph")).not.toBeNull())

    // The id is the one fact a person copies off this screen.
    expect(document.body.textContent).toContain(SEEDED_RUN)
    // The plan is a graph, and the graph is the working surface.
    expect(at("split-pane")).not.toBeNull()
    expect(at("run-not-found")).toBeNull()
    expect(at("run-error")).toBeNull()
  })

  it("tells a stale link apart from a broken backend, and names the id", async () => {
    mount("no_such_run")

    const missing = await waitFor(() => {
      const node = at("run-not-found")
      expect(node).not.toBeNull()
      return node as HTMLElement
    })

    // Not an alarm: an id out of an old link is an ordinary arrival, and only
    // the error kind interrupts a screen reader.
    expect(missing.getAttribute("role")).toBeNull()
    expect(missing.getAttribute("data-state")).toBe("notFound")
    // The id is the only part of the link the operator can take back to
    // whoever wrote it, so the state has to say it.
    expect(missing.textContent).toContain("no_such_run")
    // And a way out, because a retry here would ask the same question again.
    expect(at("run-not-found-back")?.getAttribute("href")).toBe("/runs")
    expect(at("run-error")).toBeNull()
  })

  it("calls a failed read a failure, and offers the retry", async () => {
    seedFails = true
    mount(SEEDED_RUN)

    const failed = await waitFor(() => {
      const node = at("run-error")
      expect(node).not.toBeNull()
      return node as HTMLElement
    })

    // The one kind that interrupts: something the operator was looking at is
    // not there, and they are told rather than left to find the sentence.
    expect(failed.getAttribute("role")).toBe("alert")
    expect(failed.getAttribute("data-state")).toBe("error")
    expect(failed.textContent).toContain("the swarm is unreachable")
    expect(at("run-retry")).not.toBeNull()
    expect(at("run-not-found")).toBeNull()
  })

  it("says whose run this is, in the voice a value is written in", async () => {
    mount(SEEDED_RUN)

    await waitFor(() => expect(at("run-graph")).not.toBeNull())

    /* The page is arrived at from a list that mixes projects, so the header
       says which project before it says anything else — and both halves of
       that line are values rather than prose, so both change voice. A bare
       interpolated string is what the other four detail pages do not do. */
    const header = at("page-header") as HTMLElement
    const values = header.querySelectorAll("p span")

    expect(values.length).toBeGreaterThanOrEqual(2)
    expect(header.textContent).toContain("billing-api")
  })
})
