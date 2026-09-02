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

import { CostPage } from "./cost-page"

/* The screen serves the mock seed, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a test
   of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws when every element it measures is zero — which, in jsdom, they all
   are. This screen has no pane group of its own; the shell's rail is one, and
   that is enough to throw. Stubbing it costs this test nothing it could have
   had: jsdom computes no layout, so nothing here was ever going to check one.
   The height chain is hand-traced in `cost-page.module.css`. */
vi.mock("react-resizable-panels", () => ({
  Group: ({ children, className }: { children: ReactNode; className?: string }) => (
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
})

const rootRoute = createRootRoute({ component: CostPage })
const blank = () => null
const routeTree = rootRoute.addChildren(
  [
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
  )
)

const find = (selector: string) => document.querySelector(selector)
const all = (selector: string) =>
  Array.from(document.querySelectorAll(selector))
const text = (selector: string) => find(selector)?.textContent ?? ""

async function screenReady() {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["project-admin"]}>
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

  await waitFor(() => expect(all('[data-test="cost-stat"]')).toHaveLength(3))
}

describe("the cost report, end to end over the seed", () => {
  it("draws all three readings and both breakdowns rather than a blank strip", async () => {
    // The failure this project has actually shipped from this shape: every gate
    // green on a screen that draws nothing. jsdom cannot see layout, so what is
    // asserted is that every region is in the document once the query answers.
    await screenReady()

    expect(find('[data-test="cost-by-app"]')).not.toBeNull()
    expect(find('[data-test="cost-failures"]')).not.toBeNull()
    expect(all('[data-test="spend-by-app"] li')).toHaveLength(5)
    expect(all('[data-test="failure-analytics"] li')).toHaveLength(3)
    expect(find('[data-test="spend-empty"]')).toBeNull()
  })

  it("keeps each reading at the precision that reading is worth", async () => {
    // Three precisions on one screen, and each is a decision: cents for a price
    // per success, whole dollars for a day, whole percent for a cap.
    await screenReady()

    expect(text('[data-stat="per-success"]')).toContain("$0.42")
    expect(text('[data-stat="per-day"]')).toContain("$148")
    expect(text('[data-stat="proxy-budget"]')).toContain("67%")
  })

  it("keeps the sentence under every figure", async () => {
    await screenReady()

    expect(text('[data-stat="per-success"]')).toContain(
      "key business metric — per successful task, not per call"
    )
    expect(text('[data-stat="per-day"]')).toContain(
      "86% of tasks — green gate"
    )
    expect(text('[data-stat="proxy-budget"]')).toContain(
      "$148 / $220 · kill-switch at cap"
    )
  })

  it("draws the cap without a hue while there is nothing to decide", async () => {
    await screenReady()

    // Two thirds spent is a fact, not a decision — so the tile that replaced
    // the old progress bar looks exactly like the old one did at this reading.
    // The hue only arrives at 85%, where "kill-switch at cap" stops being a
    // note and starts being a forecast.
    expect(find('[data-test="proxy-budget-meter"]')?.getAttribute("data-heat")).toBe("ok")
    expect(find('[data-stat="proxy-budget"]')?.getAttribute("data-heat")).toBe("ok")
  })

  it("hides the drawn bar from the a11y tree, because the tile already says it", async () => {
    await screenReady()

    // Nothing on this screen is announced only as a length.
    expect(
      find('[data-test="proxy-budget-meter"]')?.getAttribute("aria-hidden")
    ).toBe("true")
  })

  it("keeps the seeded-data mark", async () => {
    await screenReady()

    // The figures above are fictional and have to say so.
    expect(text('[data-test="cost-mock-mark"]')).toContain(
      "mock snapshot · VITE_USE_MOCK"
    )
  })

  it("names the region headings the product names them", async () => {
    await screenReady()

    expect(text('[data-test="cost-by-app"] h2')).toContain(
      "spend by app"
    )
    expect(text('[data-test="cost-failures"] h2')).toContain("where runs fail")
  })

  it("reads a failure rate as whole percent beside its sentence", async () => {
    await screenReady()

    const first = all('[data-test="failure-analytics"] li')[0]
    expect(first.textContent).toContain("planner")
    expect(first.textContent).toContain("11%")
    expect(first.textContent).toContain("types mismatch most often")
  })

  it("draws the week of spend beside the sentence that says it", async () => {
    await screenReady()

    const section = find('[data-test="cost-by-day"]')
    expect(section).not.toBeNull()
    expect(text('[data-test="cost-by-day"] h2')).toContain("spend by day")

    // Seven columns, one per day, and the last is today.
    const bars = all('[data-test="cost-by-day"] [data-test="bar-series-bar"]')
    expect(bars).toHaveLength(7)
    expect(bars[6].getAttribute("data-key")).toBe("today")

    // The figure states the reading in words; the chart's accessible name is
    // the same sentence, so nothing on this screen is a shape alone.
    const band = text('[data-test="spend-by-day"]')
    expect(band).toContain("over the last 7 days")
    expect(band).toContain("a day")
    expect(band).toContain("heaviest")
    expect(
      find('[data-test="cost-by-day"] [data-test="bar-series"]')?.getAttribute("aria-label")
    ).toContain("over the last 7 days")
  })
})
