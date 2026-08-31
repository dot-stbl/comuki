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
import { render, screen } from "@testing-library/react"
import { beforeAll, describe, expect, it } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"
import { ObservabilityPage } from "@/domains/observability/pages/observability-page"
import { TestSession } from "@/shared/session/test-session"

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
  ["/", "/runs", "/tasks", "/settings", "/observability"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderPage() {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/observability"] }),
  })

  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={["operator"]}>
        <QueryClientProvider client={new QueryClient()}>
          <SlotContext value={<ObservabilityPage />}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

/**
 * The whole screen, mounted the way the route mounts it.
 *
 * jsdom computes no layout, so this cannot prove the page is not a blank strip
 * — that is what the hand-traced height chain in the stylesheet is for. What it
 * does prove is that the composition holds together: the query resolves through
 * the mock, the two sections render, and the section says *why* it is links.
 */
describe("the observability screen", () => {
  it("renders both sections and names itself once", async () => {
    renderPage()

    await screen.findByRole("heading", { name: "Boards" })
    expect(
      (await screen.findByRole("heading", { level: 1 })).textContent
    ).toBe("Observability")

    expect(
      document.querySelector('[data-test="observability-boards"]')
    ).not.toBeNull()
    expect(
      document.querySelector('[data-test="observability-connect"]')
    ).not.toBeNull()
  })

  it("says on the page why the boards are links rather than embeds", async () => {
    renderPage()
    await screen.findByRole("heading", { name: "Boards" })

    // The reason is worth saying, so it is said: infra metrics and a run's own
    // timeline are read on different clocks by people asking different
    // questions. Without that line the section reads as thin rather than
    // deliberate.
    expect(
      screen.getByText(/read on different clocks by people asking different/)
    ).toBeTruthy()
    expect(document.querySelector("iframe")).toBeNull()
  })

  it("counts what is reachable rather than what exists", async () => {
    renderPage()
    await screen.findByRole("heading", { name: "Boards" })

    // Two of three: the cost board's definition is in our repo and has not been
    // imported here, which is a different fact from the board not existing.
    expect(
      document.querySelectorAll('[data-test="board-link"]')
    ).toHaveLength(2)
    expect(
      document.querySelectorAll('[data-test="board-not-imported"]')
    ).toHaveLength(1)
  })
})
