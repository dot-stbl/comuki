import type { ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest"

// `useLogoutMutation` reads `env.useMock` and routes to the kubb client when
// the operator is pointed at a real backend; in tests the variable is unset
// and the kubb client would throw on first call. Force mock mode so the
// mutation routes through `signOutMock` — exactly what the rail's old direct
// call already did.
vi.mock("@/shared/config/env", () => ({
  env: { useMock: true, apiBaseUrl: "", oidcProvider: null },
}))

import { RailAccount } from "@/app/layout/rail-account"
import { guardSession } from "@/domains/auth"
import { parseLoginSearch } from "@/domains/auth/model/landing"
import {
  clearMockAuth,
  expireMockSession,
  isMockSignedIn,
  resetMockAuth,
} from "@/shared/api/mock/auth.store"
import { TestSession } from "@/shared/session/test-session"

/* The rail's account menu is a react-aria overlay, and jsdom ships neither of
   the observers an overlay positions itself with. */
beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
})

afterEach(() => {
  resetMockAuth()
})

/**
 * The real guard on a real router.
 *
 * `beforeLoad` on the root is the claim under test — that a screen the guard
 * has never heard of is covered anyway — so the tree here deliberately holds
 * routes this test never names, and none of them carry a check of their own.
 *
 * `chrome` rides in the root's component so anything shell-shaped under test
 * (the account menu) sits inside router context, the way it does in the app.
 */
function buildRouter(initial: string, chrome?: ReactNode) {
  const rootRoute = createRootRoute({
    beforeLoad: ({ location }) => {
      guardSession(location)
    },
    component: () => (
      <>
        {chrome}
        <Outlet />
      </>
    ),
  })

  const guarded = ["/", "/runs", "/queue", "/cost"].map((path) =>
    createRoute({
      getParentRoute: () => rootRoute,
      path,
      component: () => <p>{`screen ${path}`}</p>,
    })
  )

  const login = createRoute({
    getParentRoute: () => rootRoute,
    path: "/login",
    validateSearch: parseLoginSearch,
    component: () => <p>the sign-in screen</p>,
  })

  return createRouter({
    routeTree: rootRoute.addChildren([...guarded, login]),
    history: createMemoryHistory({ initialEntries: [initial] }),
  })
}

async function renderAt(initial: string) {
  const router = buildRouter(initial)
  render(<RouterProvider router={router} />)
  await vi.waitFor(() => expect(router.state.status).toBe("idle"))
  return router
}

describe("the guard, on the routes it was never told about", () => {
  it("lets a signed-in shift reach the screen it asked for", async () => {
    resetMockAuth()

    const router = await renderAt("/runs")

    expect(await screen.findByText("screen /runs")).not.toBeNull()
    expect(router.state.location.pathname).toBe("/runs")
  })

  it("turns an unidentified visitor away from a screen with no check of its own", async () => {
    clearMockAuth()

    const router = await renderAt("/cost")

    expect(await screen.findByText("the sign-in screen")).not.toBeNull()
    expect(router.state.location.pathname).toBe("/login")
  })

  it("keeps the path they wanted, so signing in puts them back", async () => {
    clearMockAuth()

    const router = await renderAt("/queue")

    await screen.findByText("the sign-in screen")
    expect(router.state.location.search).toMatchObject({ redirect: "/queue" })
  })

  it("names the arrival when the session was lost rather than left", async () => {
    expireMockSession()

    const router = await renderAt("/runs")

    await screen.findByText("the sign-in screen")
    expect(router.state.location.search).toMatchObject({
      reason: "expired",
      redirect: "/runs",
    })
  })

  it("does not stand between a signed-out visitor and the sign-in screen", async () => {
    clearMockAuth()

    const router = await renderAt("/login")

    expect(await screen.findByText("the sign-in screen")).not.toBeNull()
    expect(router.state.location.search).toEqual({})
  })
})

describe("signing out", () => {
  async function openAccountMenu() {
    const router = buildRouter("/", <RailAccount />)
    const user = userEvent.setup()

    render(
      <TestSession>
        <QueryClientProvider client={new QueryClient()}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </TestSession>
    )

    await user.click(await screen.findByRole("button", { name: /Account/ }))
    return { router, user }
  }

  it("clears the session and lands on the quiet confirmation", async () => {
    resetMockAuth()
    expect(isMockSignedIn()).toBe(true)

    const { router, user } = await openAccountMenu()
    await user.click(await screen.findByRole("menuitem", { name: "Sign out" }))

    // The order is the point: cleared first, landed second. A navigation on its
    // own left the shell holding a signed-in shift behind a sign-in screen.
    // The rail now goes through `useLogoutMutation` (`mutateAsync` then navigate),
    // so the seed-clear happens on the microtask the click schedules — wait for
    // the mock to register the cleared session, then for the route change.
    await vi.waitFor(() => expect(isMockSignedIn()).toBe(false))
    await vi.waitFor(() => expect(router.state.location.pathname).toBe("/login"))
    expect(router.state.location.search).toMatchObject({ reason: "signed-out" })
  })

  it("leaves no return path, because leaving on purpose is not being sent away", async () => {
    resetMockAuth()

    const { router, user } = await openAccountMenu()
    await user.click(await screen.findByRole("menuitem", { name: "Sign out" }))

    await vi.waitFor(() => expect(router.state.location.pathname).toBe("/login"))
    expect(router.state.location.search).not.toHaveProperty("redirect")
  })
})
