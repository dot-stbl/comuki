import type { ReactNode } from "react"
import { createContext, useContext } from "react"
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

import { RequirePermission } from "@/app/layout/require-permission"
import { ThemeProvider } from "@/app/theme-provider"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* The denied branch renders the whole shell on purpose — the useful thing to do
   with a closed screen is leave it — so the test has to stand up everything the
   shell needs: a router for the rail's links, a query client for its live
   counts, a theme for the account menu, and a session to be denied by.

   jsdom has no ResizeObserver and the resizable rail wants one. */
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
    "/tasks",
    "/runs",
    "/approvals",
    "/cost",
    "/knowledge",
    "/settings",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderGated(node: ReactNode, roles: Role[]) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles}>
        <QueryClientProvider client={new QueryClient()}>
          <SlotContext value={node}>
            <RouterProvider router={router} />
          </SlotContext>
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )
}

const screenBody = <p>the settings screen</p>

describe("RequirePermission", () => {
  it("renders the screen when the session may open it", async () => {
    renderGated(
      <RequirePermission permission="settings.live" title="Settings">
        {screenBody}
      </RequirePermission>,
      ["project-admin"]
    )

    expect(await screen.findByText("the settings screen")).not.toBeNull()
    expect(document.querySelector("[data-test='forbidden-state']")).toBeNull()
  })

  it("swaps the screen for the fourth state when it may not", async () => {
    renderGated(
      <RequirePermission permission="settings.live" title="Settings">
        {screenBody}
      </RequirePermission>,
      ["member"]
    )

    const state = await screen.findByText(/Settings is closed to your roles/)
    expect(state).not.toBeNull()
    expect(screen.queryByText("the settings screen")).toBeNull()
  })

  it("names the roles that would open it, rather than only refusing", async () => {
    renderGated(
      <RequirePermission permission="plans.approve" title="Approvals">
        {screenBody}
      </RequirePermission>,
      ["member"]
    )

    expect(
      await screen.findByText(/needs approver, project-admin or platform-admin/)
    ).not.toBeNull()
  })

  it("keeps the shell, so a closed screen is somewhere you can leave", async () => {
    renderGated(
      <RequirePermission permission="cost.view" title="Cost & failures">
        {screenBody}
      </RequirePermission>,
      ["viewer"]
    )

    await screen.findByText(/Cost & failures is closed to your roles/)

    // The rail is still there with whatever this role *can* reach, and the
    // header still names the screen and carries the rail control.
    expect(screen.getByRole("link", { name: /Live runs/ })).not.toBeNull()
    expect(
      screen.getByRole("heading", { level: 1, name: "Cost & failures" })
    ).not.toBeNull()
  })

  it("is a courtesy, not a boundary: the URL still gates on the same act", async () => {
    // A viewer holds runs.view, so the same wrapper that closed cost opens runs
    // — one permission, one answer, whether it is asked by the rail or by a
    // typed URL.
    renderGated(
      <RequirePermission permission="runs.view" title="Live runs">
        {screenBody}
      </RequirePermission>,
      ["viewer"]
    )

    expect(await screen.findByText("the settings screen")).not.toBeNull()
  })
})
