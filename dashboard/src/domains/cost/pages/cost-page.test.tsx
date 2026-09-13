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
    "/projects/$projectId",
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

  // Wait for the three headline tiles to mount — the screen's signature.
  await waitFor(() =>
    expect(all('[data-test="cost-tiles"] > article')).toHaveLength(3)
  )
}

describe("the cost report, end to end over the seed", () => {
  it("draws all six widgets on one screen — every headline reading and every breakdown", async () => {
    await screenReady()

    // Headline tiles.
    expect(find('[data-test="total-spend"]')).not.toBeNull()
    expect(find('[data-test="forecast-widget"]')).not.toBeNull()
    expect(find('[data-test="budget-progress"]')).not.toBeNull()

    // Period toggle — three options, day pressed by default.
    expect(
      all('[data-test="period-toggle-option"][aria-pressed="true"]')
    ).toHaveLength(1)

    // Breakdowns.
    expect(find('[data-test="cost-by-day"]')).not.toBeNull()
    expect(find('[data-test="cost-by-model"]')).not.toBeNull()
    expect(find('[data-test="top-projects-section"]')).not.toBeNull()
    expect(find('[data-test="cost-by-app"]')).not.toBeNull()
    expect(find('[data-test="cost-failures"]')).not.toBeNull()

    // Spend-by-model has at least three rows — the minimum lineup.
    expect(
      all('[data-test="spend-by-model-row"]').length
    ).toBeGreaterThanOrEqual(3)

    // Top-projects renders seven rows (the seed's limit).
    expect(all('[data-test="top-projects-row"]').length).toBe(7)
  })

  it("states the period total as the headline figure, with delta vs previous", async () => {
    await screenReady()

    const figure = text('[data-test="total-spend"]')
    expect(figure).toContain("$148.20")
    // Day's burn rate is derived from total / period-days.
    expect(figure).toContain("$148.20 / day")
    // Delta vs yesterday is a small negative — the seeded previous is $152.7.
    expect(find('[data-test="total-spend-delta"]')?.textContent).toMatch(
      /▼\s?3%/
    )
  })

  it("says end-of-period for the forecast and renders a heat reading", async () => {
    await screenReady()

    const forecast = text('[data-test="forecast-widget"]')
    expect(forecast).toContain("Forecast")
    expect(forecast).toContain("end of day")
    expect(forecast).toMatch(/\d+%\s+of\s+\$220 cap/)
    // Day view: $148.2 / $220 = 67% — ok, no hue.
    expect(
      find('[data-test="forecast-widget"]')?.getAttribute("data-heat")
    ).toBe("ok")
  })

  it("shows today's burn and month-to-date as the budget's two readings", async () => {
    await screenReady()

    const budget = text('[data-test="budget-progress"]')
    expect(budget).toContain("$148")
    expect(budget).toContain("$220")
    expect(budget).toContain("today")
    expect(budget).toContain("month-to-date")
    // 67% today — ok.
    expect(
      find('[data-test="budget-progress"]')?.getAttribute("data-heat")
    ).toBe("ok")
  })

  it("renders three model rows with the project's actual lineup", async () => {
    await screenReady()

    const rows = all('[data-test="spend-by-model-row"]')
    const models = rows.map((node) => node.getAttribute("data-model") ?? "")
    expect(models).toContain("glm-5.2")
    expect(models).toContain("glm-4.5")
    expect(models).toContain("MiniMax-M3")
  })

  it("ranks the top projects by spend and lands the runaway at the top", async () => {
    await screenReady()

    const rows = all('[data-test="top-projects-row"]')
    const projectIds = rows.map(
      (node) => node.getAttribute("data-project") ?? ""
    )
    // The runaway is the seed's biggest spender — its 12× median lands it
    // ahead of comuki, atlas, kafka, even though those projects have higher
    // caps. The ranking is by spend, not by cap.
    expect(projectIds[0]).toBe("p_prometheus")
    // The cap column surfaces sentinel-vault's zero cap on hover even when
    // the row is sliced off the visible top-N — proving the seed kept the
    // project in the data set rather than dropping it.
    expect(
      find(
        '[data-test="top-projects-row"][data-project="p_prometheus"]'
      )?.querySelector('[data-test="top-projects-spend"]')?.textContent
    ).toBe("$31")
  })

  it("names the regions the product names them", async () => {
    await screenReady()

    expect(text('[data-test="cost-by-day"] h2')).toContain("spend by day")
    expect(text('[data-test="cost-by-model"] h2')).toContain("spend by model")
    expect(text('[data-test="top-projects-section"] h2')).toContain(
      "top projects"
    )
    expect(text('[data-test="cost-by-app"] h2')).toContain("spend by app")
    expect(text('[data-test="cost-failures"] h2')).toContain("where runs fail")
  })

  it("keeps the seeded-data mark", async () => {
    await screenReady()

    expect(text('[data-test="cost-mock-mark"]')).toContain(
      "mock snapshot · VITE_USE_MOCK"
    )
  })

  it("flips the period with the toggle and re-shapes the byDay length", async () => {
    const user = (await import("@testing-library/user-event")).default.setup()
    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: ["/"] }),
    })

    render(
      <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
        <TestSession roles={["project-admin"]}>
          <QueryClientProvider
            client={
              new QueryClient({
                defaultOptions: { queries: { retry: false } },
              })
            }
          >
            <RouterProvider router={router} />
          </QueryClientProvider>
        </TestSession>
      </ThemeProvider>
    )

    // Day: 7 columns (the seed's `seedDayAxis()` length).
    await waitFor(() =>
      expect(
        all('[data-test="cost-by-day"] [data-test="bar-series-bar"]')
      ).toHaveLength(7)
    )

    // Flip the period — week starts where day does, but the byDay stays at
    // 7 columns (the seed keeps the day axis for week view); the figure
    // becomes the week total.
    const options = all('[data-test="period-toggle-option"]')
    const weekButton = options.find(
      (button) => button.getAttribute("data-value") === "week"
    ) as HTMLButtonElement | undefined
    expect(weekButton).toBeDefined()
    await user.click(weekButton!)

    await waitFor(() =>
      expect(weekButton!.getAttribute("aria-pressed")).toBe("true")
    )
    await waitFor(() =>
      expect(text('[data-test="total-spend"]')).toContain("$917.80")
    )
  })
})
