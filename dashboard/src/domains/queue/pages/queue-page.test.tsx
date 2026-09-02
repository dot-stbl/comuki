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
import { beforeAll, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { TestSession } from "@/shared/session/test-session"

import { QueuePage } from "./queue-page"

/* The screen serves the mock seeds, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a
   test of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws `No layout data found for index 0` when every element it measures is
   zero — which, in jsdom, they all are. It is not this screen: `RunsPage`, in
   the same shell with the same nested pane group, throws identically. So the
   pane group is stubbed to bare boxes, which costs this test nothing it could
   have had. jsdom computes no layout, so no test in this file was ever going
   to check one; what it checks is that the screen's data path reaches rows,
   and that survives the stub intact. The height chain itself is hand-traced,
   in the comment at the top of `queue-page.module.css`. */
vi.mock("react-resizable-panels", () => ({
  // Only `children` and `className` reach the DOM; the layout props are
  // destructured away rather than spread, so React does not warn about them.
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
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1400,
  })
})

const rootRoute = createRootRoute({ component: QueuePage })
const blank = () => null
const routeTree = rootRoute.addChildren([
  ...[
    "/tasks",
    "/runs",
    "/queue",
    "/approvals",
    "/cost",
    "/sources",
    "/knowledge",
    "/verify",
    "/settings",
    "/projects",
    "/identity",
    "/compute",
    "/models",
    "/observability",
    "/components",
    "/login",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  ),
  createRoute({
    getParentRoute: () => rootRoute,
    path: "/runs/$runId",
    component: blank,
  }),
])

function renderScreen() {
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
          <RouterProvider router={router} />
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const find = (selector: string) => document.querySelector(selector)
const all = (selector: string) => Array.from(document.querySelectorAll(selector))

/** The screen once its one query has answered — both halves on the board. */
async function boardReady() {
  renderScreen()
  await waitFor(() =>
    expect(all('[data-test="data-table"]')).toHaveLength(2)
  )
}

describe("the queue screen, end to end over the seeds", () => {
  it("puts both halves on the split, each with its own toolbar", async () => {
    await boardReady()

    // The shell's rail is itself a pane group, so this screen's own is second.
    expect(all('[data-test="split-pane"]').length).toBeGreaterThanOrEqual(2)
    // Two toolbars: the halves are narrowed independently, on purpose.
    expect(all('[data-test="data-table-toolbar"]')).toHaveLength(2)
  })

  it("renders rows in both tables rather than two empty bands", async () => {
    // The failure this project has actually shipped from this shape: every
    // gate green on a screen that draws nothing. jsdom cannot see layout, so
    // what is asserted is that rows exist at all once the port has a depth.
    await boardReady()

    expect(all('[data-test="age-meter"]').length).toBeGreaterThan(0)
    expect(all('[data-test="lease-meter"]').length).toBeGreaterThan(0)
    expect(find('[data-test="data-table-empty"]')).toBeNull()
    expect(find('[data-test="worker-empty"]')).toBeNull()
  })

  it("opens on the longest unclaimed wait", async () => {
    await boardReady()

    // The seed's worst case: 43:32 queued on a profile no worker in that pool
    // runs. If triage ever stops being the order rows arrive in, this moves.
    expect(all('[data-test="age-meter"]')[0].textContent).toBe("43:32")
  })

  it("links a work item out to the run it belongs to", async () => {
    await boardReady()

    const link = find('[data-test="queue-run-link"]')
    expect(link?.getAttribute("href")).toMatch(/^\/runs\/[0-9a-f]{8}$/)
  })

  it("says how much is queued, how much is late, and what the pool is", async () => {
    await boardReady()

    // The seeded shift: fourteen queued, three of them past five minutes,
    // eleven workers with three idle, and one holding a lease it stopped
    // heartbeating on — the pair of failures the screen exists to catch.
    const header = find('[data-test="page-header"]')
    expect(header?.textContent).toContain("14 queued")
    expect(header?.textContent).toContain("3 unclaimed over five minutes")
    expect(header?.textContent).toContain("11 workers, 3 idle")
    expect(header?.textContent).toContain("1 without a heartbeat")
  })

  it("offers the pool's collapse, and starts expanded", async () => {
    await boardReady()

    const toggle = find('[data-test="pool-toggle"]') as HTMLElement
    // The words moved to the tooltip, so the reading to assert is the name the
    // control answers to — which is what an operator is told to look for and
    // what assistive tech reads out, tooltip open or not.
    expect(toggle.getAttribute("aria-label")).toBe("Collapse pool")
    expect(toggle.getAttribute("aria-controls")).toBe("pool")
    expect(toggle.getAttribute("aria-expanded")).toBe("true")
    expect(find('[data-test="pool-strip"]')).toBeNull()
  })

  it("draws the week of depth above the split, beside the sentence", async () => {
    await boardReady()

    const band = find('[data-test="queue-depth"]')
    expect(band).not.toBeNull()

    // Seven columns, today last, and the figure says the same reading the
    // header above it is saying — one number, one direction, one source.
    const bars = all('[data-test="queue-depth"] [data-test="bar-series-bar"]')
    expect(bars).toHaveLength(7)
    expect(bars[bars.length - 1].getAttribute("data-key")).toBe("today")

    const figure = band?.textContent ?? ""
    expect(figure).toContain("14 queued now")
    expect(figure).toContain("the week ran 4–9")
    expect(figure).toContain("deepest of the week today")

    // The chart's accessible name is the same reading in words, so the trend
    // is not a shape a screen reader cannot follow.
    expect(
      find('[data-test="queue-depth"] [data-test="bar-series"]')?.getAttribute("aria-label")
    ).toContain("14 queued today")
  })
})

/* ------------------------------------------------------------------ *
 * The two halves, narrowed from the URL.
 *
 * The screen is one mechanism seen from both ends, and the parameters follow:
 * `?q=` narrows the queue, `?w=` narrows the pool, and neither may narrow the
 * other. That is the whole reason there are two — a work item id and a worker
 * id answer to different columns, so one shared value would empty whichever
 * half it was not written for.
 * ------------------------------------------------------------------ */

/** The screen with the route driving both halves, exactly as `/queue` does. */
function renderNarrowed(props: {
  search?: string
  workerSearch?: string
  onSearchChange?: (next: string) => void
  onWorkerSearchChange?: (next: string) => void
}) {
  const tree = createRootRoute({ component: () => <QueuePage {...props} /> })
  const router = createRouter({
    routeTree: tree,
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
          <RouterProvider router={router} />
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

/** Both search boxes, in panel order: the queue's, then the pool's. */
const searches = () =>
  Array.from(
    document.querySelectorAll<HTMLInputElement>(
      '[data-test="data-table-search"]'
    )
  )

describe("the queue screen, narrowed from its own address", () => {
  it("puts `q` in the queue half and leaves the pool alone", async () => {
    renderNarrowed({
      search: "wi_0101",
      onSearchChange: () => {},
      onWorkerSearchChange: () => {},
    })
    await waitFor(() => expect(searches()).toHaveLength(2))

    expect(searches().map((box) => box.value)).toEqual(["wi_0101", ""])
    // One work item, found by its own id rather than by scanning anything.
    expect(find('[data-test="queue-count"]')?.textContent).toContain("1 shown")
  })

  it("puts `w` in the pool half and leaves the queue alone", async () => {
    renderNarrowed({
      workerSearch: "wk_e34d",
      onSearchChange: () => {},
      onWorkerSearchChange: () => {},
    })
    await waitFor(() => expect(searches()).toHaveLength(2))

    expect(searches().map((box) => box.value)).toEqual(["", "wk_e34d"])
    expect(find('[data-test="worker-count"]')?.textContent).toContain(
      "1 of 11 workers"
    )
  })

  it("finds every container on an image, which is what a digest is asked for", async () => {
    renderNarrowed({
      workerSearch: "sha256:41b7de",
      onSearchChange: () => {},
      onWorkerSearchChange: () => {},
    })
    await waitFor(() => expect(searches()).toHaveLength(2))

    // The seed puts exactly one container a release behind, and that is why
    // it is draining. A digest that found nothing would be a shape with no
    // destination, which is the one thing the catalogue must not carry.
    expect(find('[data-test="worker-count"]')?.textContent).toContain(
      "1 of 11 workers"
    )
  })

  it("holds both at once without either dropping the other", async () => {
    renderNarrowed({
      search: "wi_0101",
      workerSearch: "wk_e34d",
      onSearchChange: () => {},
      onWorkerSearchChange: () => {},
    })
    await waitFor(() => expect(searches()).toHaveLength(2))

    expect(searches().map((box) => box.value)).toEqual(["wi_0101", "wk_e34d"])
  })

  it("keeps both values itself when nobody is driving them", async () => {
    await boardReady()

    // Unchanged behaviour for a screen rendered without the route's halves:
    // the panels narrow themselves and nothing outside them hears.
    expect(searches().map((box) => box.value)).toEqual(["", ""])
  })
})
