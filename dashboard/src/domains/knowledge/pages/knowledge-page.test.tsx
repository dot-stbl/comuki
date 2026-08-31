import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { TestSession } from "@/shared/session/test-session"

import { KnowledgePage } from "./knowledge-page"

/* The screen serves the mock seeds, and whether it does is normally an
   environment variable that is not committed. Pinning it here makes this a test
   of the screen rather than of whoever's `.env.local` is on disk. */
vi.mock("@/shared/config/env", () => ({ env: { useMock: true } }))

/* `react-resizable-panels` v4 solves for a layout in a `useLayoutEffect` and
   throws when every element it measures is zero — which, in jsdom, they all
   are. This screen has no pane group of its own; the shell's rail is one, and
   that is enough to throw. The height chain is hand-traced in
   `knowledge-page.module.css` instead, because jsdom computes no layout and
   nothing here was ever going to check one. */
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

/* The eval table virtualizes, so it needs a scroll port with a depth and
   something watching it. Same stubs as `data-table.test.tsx`, for the same
   reason: without them the body renders no rows and an assertion about the
   harness would pass against an empty frame. */
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

/* The product marks its own elements with `data-test`, not `data-testid`. */
const find = (selector: string) => document.querySelector(selector)
const all = (selector: string) =>
  Array.from(document.querySelectorAll(selector))
const entries = () => all('[data-test="knowledge-entry"]')
const sheet = () => find('[data-test="knowledge-sheet"]')
const searchBox = () =>
  find('[data-test="knowledge-search"]') as HTMLInputElement

const blank = () => null

function renderScreen(focus?: string) {
  const rootRoute = createRootRoute({
    component: () => <KnowledgePage focus={focus} />,
  })
  const routeTree = rootRoute.addChildren(
    ["/settings", "/runs", "/knowledge"].map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    )
  )
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["member"]}>
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

async function ready(focus?: string) {
  renderScreen(focus)
  await waitFor(() => expect(entries().length).toBeGreaterThan(0))
}

describe("the knowledge screen, end to end over the seeds", () => {
  it("draws all three regions rather than a blank strip", async () => {
    // The failure this project has actually shipped from this shape: every gate
    // green on a screen that draws nothing. jsdom sees no layout, so what is
    // asserted is that each region rendered something at all.
    await ready()

    expect(find('[data-test="knowledge-revision"]')).not.toBeNull()
    expect(find('[data-test="knowledge-entries"]')).not.toBeNull()
    expect(find('[data-test="knowledge-eval"]')).not.toBeNull()
    expect(entries()).toHaveLength(7)
  })

  it("keeps the three revision readings the screen exists to state", async () => {
    await ready()

    const region = find('[data-test="knowledge-revision"]')
    expect(region?.textContent).toContain("rules@a1b9e0")
    expect(region?.textContent).toContain("sdk@2.4.1 · updated 2h ago")
    expect(region?.textContent).toContain("3 hard · 2 soft")
    expect(region?.textContent).toContain("every run pins the rule set + SDK")
  })

  it("puts the search in the header band, where it cannot scroll away", async () => {
    await ready()

    const band = find('[data-test="page-header-filters"]')
    expect(band?.querySelector('[data-test="knowledge-search"]')).not.toBeNull()
  })

  it("narrows the list from the field and says so when nothing matches", async () => {
    await ready()

    fireEvent.change(searchBox(), { target: { value: "web-app" } })
    await waitFor(() => expect(entries()).toHaveLength(1))

    fireEvent.change(searchBox(), { target: { value: "nothing matches this" } })
    await waitFor(() =>
      expect(find('[data-test="knowledge-empty"]')).not.toBeNull()
    )
    expect(screen.getByText("No matches")).toBeTruthy()
  })

  it("arrives narrowed when the address bar carried a query", async () => {
    // `/knowledge?q=web-app` — the link somebody pasted into a review.
    await ready("web-app")

    expect(entries()).toHaveLength(1)
    // And it is the operator's from that moment: the value is in the field
    // they would clear it from, not applied invisibly behind it.
    expect(searchBox().value).toBe("web-app")
  })

  it("opens the entry sheet from a row and closes it again", async () => {
    await ready()

    expect(sheet()).toBeNull()
    fireEvent.click(entries()[0])

    await waitFor(() => expect(sheet()).not.toBeNull())
    expect(sheet()?.textContent).toContain("api-errors")

    fireEvent.click(screen.getByRole("button", { name: "Close the entry" }))
    await waitFor(() => expect(sheet()).toBeNull())
  })
})
