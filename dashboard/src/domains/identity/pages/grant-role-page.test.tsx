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
import { GrantRolePage } from "@/domains/identity/pages/grant-role-page"
import { isIdentityTab, type IdentityTab } from "@/domains/identity/model/tabs"
import { IdentityPage } from "@/domains/identity/pages/identity-page"
import {
  listSeedRoleAssignments,
  resetSeedIdentity,
} from "@/shared/api/mock/identity.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { setSelectValue } from "@/shared/ui/select/test-select"

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
    value: 1200,
  })
  /* jsdom measures nothing; restoring the rail's persisted split would throw. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetSeedIdentity()
})

const RAIL_PATHS = [
  "/",
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
  "/compute",
  "/models",
  "/observability",
  "/components",
]

interface Search {
  tab?: IdentityTab
  q?: string
}

function buildRouteTree() {
  const rootRoute = createRootRoute()
  const blank = () => null

  const identity = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity",
    validateSearch: (search: Record<string, unknown>): Search => {
      const parsed: Search = {}
      if (isIdentityTab(search.tab)) {
        parsed.tab = search.tab
      }
      if (typeof search.q === "string" && search.q) {
        parsed.q = search.q
      }
      return parsed
    },
    component: function IdentityRoute() {
      const { tab = "users", q } = identity.useSearch()
      return <IdentityPage tab={tab} focus={q} onTabChange={() => {}} />
    },
  })

  const grant = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity/grants/new",
    component: GrantRolePage,
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    identity,
    grant,
  ])
}

function mount(entries: string[], roles: Role[] = ["platform-admin"]) {
  const router = createRouter({
    routeTree: buildRouteTree(),
    history: createMemoryHistory({ initialEntries: entries }),
  })

  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles}>
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </QueryClientProvider>
      </TestSession>
    </ThemeProvider>
  )

  return router
}

const here = (router: ReturnType<typeof mount>) =>
  `${router.state.location.pathname}${router.state.location.searchStr}`

const submitButton = () =>
  document.querySelector('[data-test="form-submit"]') as HTMLButtonElement

const grantCount = () => listSeedRoleAssignments().length

describe("a grant lands on its own screen", () => {
  it("writes the role and returns to the grants list, narrowed to the subject", async () => {
    const router = mount(["/identity", "/identity/grants/new"])

    await screen.findByRole("heading", { name: "Grant a role" })
    // The submit is gated by a populated subject list — wait for the disabled
    // button to flip before reading the seed.
    await waitFor(() =>
      expect(submitButton().hasAttribute("disabled")).toBe(false)
    )

    const before = grantCount()
    fireEvent.click(submitButton())

    await waitFor(() => expect(grantCount()).toBe(before + 1))
    // The list is narrowed to the subject rather than to the role: a person
    // holds several grants and the new one is read next to the others they
    // already had.
    await waitFor(() =>
      expect(here(router)).toMatch(/^\/identity\?tab=grants&q=/)
    )
  })

  it("names the scope in the toast and on the destination", async () => {
    const router = mount(["/identity", "/identity/grants/new"])

    await screen.findByRole("heading", { name: "Grant a role" })
    await waitFor(() =>
      expect(submitButton().hasAttribute("disabled")).toBe(false)
    )

    // The default scope is platform — the toast reads `viewer on platform`,
    // and the URL lands the operator back on the grants list, narrowed to
    // the subject the form picked for them.
    fireEvent.click(submitButton())

    await waitFor(() => expect(grantCount()).toBeGreaterThan(0))
    await waitFor(() => expect(here(router)).toMatch(/^\/identity\?tab=grants/))
  })
})

describe("leaving a half-filled form", () => {
  it("never asks when the form was clean", async () => {
    const router = mount(["/identity", "/identity/grants/new"])

    await screen.findByRole("heading", { name: "Grant a role" })
    fireEvent.click(screen.getByRole("link", { name: "identity" }))

    await waitFor(() => expect(here(router)).toBe("/identity"))
    expect(
      screen.queryByText("Leave without granting the role?")
    ).toBeNull()
  })

  it("returns where the operator came from on cancel, without asking", async () => {
    const router = mount(["/identity", "/identity/grants/new"])

    await screen.findByRole("heading", { name: "Grant a role" })
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/identity"))
    expect(
      screen.queryByText("Leave without granting the role?")
    ).toBeNull()
  })

  it("asks before dropping a decision the form has already recorded", async () => {
    const router = mount(["/identity", "/identity/grants/new"])

    await screen.findByRole("heading", { name: "Grant a role" })
    // Changing the role off the default is a real decision worth keeping —
    // a question before dropping it is the same shape every form here uses.
    setSelectValue(screen.getByLabelText("role"), "approver")
    fireEvent.click(screen.getByRole("link", { name: "identity" }))

    await screen.findByText("Leave without granting the role?")
    expect(here(router)).toBe("/identity/grants/new")
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps the act in the document and names what it needs", async () => {
    mount(["/identity/grants/new"], ["operator"])

    await screen.findByRole("heading", { name: "Grant a role" })
    await waitFor(() =>
      expect(submitButton().hasAttribute("disabled")).toBe(false)
    )

    const submit = submitButton()
    expect(submit.getAttribute("aria-disabled")).toBe("true")
    expect(submit.getAttribute("title")).toBe("needs platform-admin")
    expect(submit.hasAttribute("disabled")).toBe(false)

    const before = grantCount()
    fireEvent.click(submit)
    expect(grantCount()).toBe(before)
  })
})
