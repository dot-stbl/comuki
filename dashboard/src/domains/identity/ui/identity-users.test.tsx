import { createContext, useContext, type ReactNode } from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { beforeAll, beforeEach, describe, expect, it } from "vitest"

import { buildIdentitySnapshot } from "@/domains/identity/model/identity"
import { UsersPanel } from "@/domains/identity/ui/users-panel"
import {
  listSeedUsers,
  resetSeedIdentity,
} from "@/shared/api/mock/identity.store"
import {
  API_KEYS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  USERS_SEED,
} from "@/shared/api/mock/identity.seed"
import { PLATFORM_PROJECTS_SEED } from "@/shared/api/mock/projects.seed"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* jsdom lays nothing out and the table body is virtualized, so without a port
   depth the rows — and with them the buttons under test — never render. */
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
})

const USERS = buildIdentitySnapshot(
  USERS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  API_KEYS_SEED,
  PLATFORM_PROJECTS_SEED,
  new Date("2026-08-30T09:00:00Z")
).users

/* Two of this panel's three acts are navigation now — inviting somebody and
   linking a subject are forms, and forms are pages — so it only renders inside
   a router. A memory router carrying the paths this panel points at keeps the
   test off the app's generated route tree. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/identity", "/identity/users/new", "/identity/users/$userId/link"].map(
    (path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function makeRouter() {
  return createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/identity"] }),
  })
}

function Providers({
  roles,
  router,
  children,
}: {
  roles: Role[]
  router: ReturnType<typeof makeRouter>
  children: ReactNode
}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return (
    <QueryClientProvider client={client}>
      <TestSession roles={roles}>
        <SlotContext value={children}>
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </SlotContext>
      </TestSession>
    </QueryClientProvider>
  )
}

/* The router resolves its first match asynchronously, so nothing is on screen
   until it has — hence the await on the toolbar's own row count. */
async function mount(roles: Role[] = ["platform-admin"]) {
  const router = makeRouter()
  render(
    <Providers roles={roles} router={router}>
      <UsersPanel users={USERS} />
    </Providers>
  )
  await screen.findByText(/ shown$/)
  return router
}

beforeEach(() => {
  resetSeedIdentity()
})

describe("who exists", () => {
  it("says what an account holds, including nothing", async () => {
    await mount()

    // Two projects and no platform standing — the seeded awkward case.
    expect(screen.getByText("comuki · atlas")).toBeTruthy()
  })

  it("calls an account with no provider subject local, not broken", async () => {
    await mount()

    expect(screen.getAllByText("local only").length).toBeGreaterThan(0)
  })

  it("says never rather than leaving a blank where a date would be", async () => {
    await mount()

    // Invited and has not arrived: the cell is a fact, not an empty box.
    expect(screen.getByText("never")).toBeTruthy()
  })

  it("offers the oidc link only where there is nothing linked yet", async () => {
    await mount()

    expect(
      screen.getByRole("button", {
        name: "Link an oidc subject to nadia@plexor.dev",
      })
    ).toBeTruthy()
    expect(
      screen.queryByRole("button", {
        name: "Link an oidc subject to duty@comuki.local",
      })
    ).toBeNull()
  })
})

describe("the acts that became screens", () => {
  it("makes creating a person a destination, not a modal", async () => {
    await mount()

    // A link, not a button: it has an address, it can be opened in a tab, and
    // anything that traverses links can see where it goes.
    const create = screen.getByRole("link", { name: "New user" })
    expect(create.getAttribute("href")).toBe("/identity/users/new")
  })

  it("sends the row's link action to that account's own page", async () => {
    const router = await mount()

    fireEvent.click(
      screen.getByRole("button", {
        name: "Link an oidc subject to nadia@plexor.dev",
      })
    )

    // Editing an account that exists is the one flow whose address names a
    // subject — so the page can be reloaded, sent to somebody, and come back
    // to the same account.
    await waitFor(() =>
      expect(router.state.location.pathname).toBe(
        "/identity/users/u_nadia/link"
      )
    )
  })
})

describe("switching an account off", () => {
  it("asks first, and says the grants survive it", async () => {
    await mount()

    fireEvent.click(
      screen.getByRole("button", { name: "Disable rhea@comuki.local" })
    )

    expect(screen.getByText("Disable this account?")).toBeTruthy()
    expect(screen.getByText(/Their grants stay as they are/)).toBeTruthy()
    expect(listSeedUsers().find((user) => user.id === "u_rhea")?.status).toBe(
      "active"
    )
  })

  it("disables on confirmation", async () => {
    await mount()

    fireEvent.click(
      screen.getByRole("button", { name: "Disable rhea@comuki.local" })
    )
    fireEvent.click(screen.getByRole("button", { name: "Disable" }))

    await waitFor(() =>
      expect(listSeedUsers().find((user) => user.id === "u_rhea")?.status).toBe(
        "disabled"
      )
    )
  })

  it("turns one back on without asking, because that is not destructive", async () => {
    await mount()

    fireEvent.click(
      screen.getByRole("button", { name: "Enable tomas@comuki.local" })
    )

    await waitFor(() =>
      expect(
        listSeedUsers().find((user) => user.id === "u_tomas")?.status
      ).toBe("active")
    )
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps every act in the document and names what it needs", async () => {
    await mount(["operator"])

    // Icon-only in a kit tooltip, so the refusal arrives there and the button
    // drops its native title rather than delivering it twice. The
    // `data-denied` attribute carries the reason either way.
    const create = screen.getByRole("button", { name: "New user" })
    expect(create.getAttribute("aria-disabled")).toBe("true")
    expect(create.getAttribute("data-denied")).toBe("needs platform-admin")
    expect(create.hasAttribute("disabled")).toBe(false)

    const disable = screen.getByRole("button", {
      name: "Disable rhea@comuki.local",
    })
    expect(disable.getAttribute("aria-disabled")).toBe("true")
    expect(disable.getAttribute("data-denied")).toBe("needs platform-admin")

    fireEvent.click(disable)
    expect(screen.queryByText("Disable this account?")).toBeNull()
  })
})
