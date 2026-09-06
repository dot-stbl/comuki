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
import { InviteUserPage } from "@/domains/identity/pages/invite-user-page"
import { isIdentityTab, type IdentityTab } from "@/domains/identity/model/tabs"
import { IdentityPage } from "@/domains/identity/pages/identity-page"
import {
  listSeedUsers,
  resetSeedIdentity,
} from "@/shared/api/mock/identity.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

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

  const invite = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity/users/new",
    component: InviteUserPage,
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    identity,
    invite,
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

const takenFromSeed = () =>
  listSeedUsers().map((user) => user.email.toLowerCase())

describe("a new account lands on its own screen", () => {
  it("writes the invite and returns to the users list, narrowed to the address", async () => {
    const router = mount(["/identity", "/identity/users/new"])

    await screen.findByRole("heading", { name: "New user" })

    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Ines Duarte" },
    })
    fireEvent.change(screen.getByLabelText("address"), {
      target: { value: "ines@plexor.dev" },
    })
    fireEvent.click(submitButton())

    await waitFor(() =>
      expect(
        listSeedUsers().find((user) => user.email === "ines@plexor.dev")
      ).toBeTruthy()
    )
    // The list is narrowed to the address that was just written: the operator
    // sees the row they made, with the toolbar explaining why the list is one
    // row long.
    await waitFor(() =>
      expect(here(router)).toBe("/identity?tab=users&q=ines%40plexor.dev")
    )
  })

  it("seeds the form with every address already on the platform", async () => {
    mount(["/identity/users/new"])

    await screen.findByRole("heading", { name: "New user" })

    const before = takenFromSeed()
    // Pick the second seeded account so the assertion is about the form's
    // awareness of addresses beyond the first one rendered.
    const address = before[1] ?? before[0]
    if (!address) {
      throw new Error("seed has no users to assert against")
    }

    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Somebody new" },
    })
    fireEvent.change(screen.getByLabelText("address"), {
      target: { value: address },
    })
    fireEvent.click(submitButton())

    await waitFor(() =>
      expect(screen.getByRole("alert").textContent).toContain(
        "somebody already has that address"
      )
    )
    // Nothing was written: a duplicate address is a refusal, not an update.
    expect(
      listSeedUsers().filter((user) => user.email.toLowerCase() === address)
    ).toHaveLength(1)
  })
})

describe("leaving a half-filled form", () => {
  it("asks before dropping the name and address that were typed", async () => {
    const router = mount(["/identity", "/identity/users/new"])

    await screen.findByRole("heading", { name: "New user" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Ines" },
    })
    fireEvent.click(screen.getByRole("link", { name: "identity" }))

    await screen.findByText("Leave without creating the account?")
    expect(here(router)).toBe("/identity/users/new")
  })

  it("never asks when the form was clean", async () => {
    const router = mount(["/identity", "/identity/users/new"])

    await screen.findByRole("heading", { name: "New user" })
    fireEvent.click(screen.getByRole("link", { name: "identity" }))

    // No half-filled form, so the question is never asked.
    await waitFor(() => expect(here(router)).toBe("/identity"))
    expect(
      screen.queryByText("Leave without creating the account?")
    ).toBeNull()
  })

  it("returns where the operator came from on cancel, without asking", async () => {
    const router = mount(["/identity", "/identity/users/new"])

    await screen.findByRole("heading", { name: "New user" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Ines" },
    })
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/identity"))
    expect(
      screen.queryByText("Leave without creating the account?")
    ).toBeNull()
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps the act in the document and names what it needs", async () => {
    mount(["/identity/users/new"], ["operator"])

    await screen.findByRole("heading", { name: "New user" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Ines" },
    })
    fireEvent.change(screen.getByLabelText("address"), {
      target: { value: "ines@plexor.dev" },
    })

    const submit = submitButton()
    expect(submit.getAttribute("aria-disabled")).toBe("true")
    expect(submit.getAttribute("title")).toBe("needs platform-admin")
    expect(submit.hasAttribute("disabled")).toBe(false)

    const before = listSeedUsers().length
    fireEvent.click(submit)
    expect(listSeedUsers().length).toBe(before)
  })
})
