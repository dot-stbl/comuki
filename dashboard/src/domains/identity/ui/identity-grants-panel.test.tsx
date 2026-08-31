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
import { GrantsPanel } from "@/domains/identity/ui/grants-panel"
import {
  API_KEYS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  USERS_SEED,
} from "@/shared/api/mock/identity.seed"
import {
  listSeedRoleAssignments,
  resetSeedIdentity,
} from "@/shared/api/mock/identity.store"
import { PLATFORM_PROJECTS_SEED } from "@/shared/api/mock/projects.seed"
import { ROLES, type Role } from "@/shared/session"
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
    value: 640,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1280,
  })
})

const SNAPSHOT = buildIdentitySnapshot(
  USERS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  API_KEYS_SEED,
  PLATFORM_PROJECTS_SEED,
  new Date("2026-08-30T09:00:00Z")
)

/* Writing a grant is a form, so it is a page now, so the panel's create act is
   a real link — and a link only renders inside a router. A memory router
   carrying the two paths this panel points at keeps the test off the app's
   generated route tree. */
const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/identity", "/identity/grants/new"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function Providers({
  roles,
  children,
}: {
  roles: Role[]
  children: ReactNode
}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/identity"] }),
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
  render(
    <Providers roles={roles}>
      <GrantsPanel grants={SNAPSHOT.grants} />
    </Providers>
  )
  await screen.findByText(/ shown$/)
}

beforeEach(() => {
  resetSeedIdentity()
})

describe("the grants list", () => {
  it("filters roles by the same six the form offers, and no more", async () => {
    await mount()

    // The toolbar derives its controls from the column declarations, so this
    // is the column's own option list — one constant behind the filter, the
    // sort order and the grant form alike. It lives one click in now: the row
    // carries the search and a button, and every other declared filter sits
    // behind that button.
    fireEvent.click(screen.getByRole("button", { name: /^Filters/ }))
    fireEvent.click(screen.getByRole("button", { name: /all roles/i }))
    const options = screen
      .getAllByRole("option")
      .map((option) => option.textContent?.trim())

    // Plus the "no filter" entry the toolbar adds in front.
    expect(options).toEqual(["all roles", ...ROLES])
  })

  it("carries no way to make a role anywhere on the list", async () => {
    await mount()

    expect(
      screen.queryByText(/new role|create role|add role|custom role/i)
    ).toBeNull()
  })

  it("names a platform grant by its scope and a project grant by its slug", async () => {
    await mount()

    expect(screen.getAllByText("platform").length).toBeGreaterThan(0)
    expect(screen.getAllByText("atlas").length).toBeGreaterThan(0)
  })
})

describe("revoking a grant", () => {
  it("asks before it does it", async () => {
    await mount()

    fireEvent.click(
      screen.getByRole("button", {
        name: "Revoke project-admin on atlas from duty@comuki.local",
      })
    )

    expect(screen.getByText("Revoke this grant?")).toBeTruthy()
    expect(
      listSeedRoleAssignments().some((grant) => grant.id === "g_duty_atlas")
    ).toBe(true)
  })

  it("revokes on confirmation", async () => {
    await mount()

    fireEvent.click(
      screen.getByRole("button", {
        name: "Revoke project-admin on atlas from duty@comuki.local",
      })
    )
    fireEvent.click(screen.getByRole("button", { name: "Revoke" }))

    await waitFor(() =>
      expect(
        listSeedRoleAssignments().some((grant) => grant.id === "g_duty_atlas")
      ).toBe(false)
    )
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps both acts in the document and names what they need", async () => {
    await mount(["operator"])

    // Both acts are icon-only now and both sit in a kit tooltip, so the
    // sentence arrives there rather than on a native `title` — `Button` drops
    // the attribute inside a tooltip so the refusal is not delivered twice.
    // `data-denied` is where the reason lives either way.
    const grant = screen.getByRole("button", { name: "Grant a role" })
    expect(grant.getAttribute("aria-disabled")).toBe("true")
    expect(grant.getAttribute("data-denied")).toBe("needs platform-admin")
    expect(grant.hasAttribute("disabled")).toBe(false)

    const revoke = screen.getByRole("button", {
      name: "Revoke project-admin on atlas from duty@comuki.local",
    })
    expect(revoke.getAttribute("aria-disabled")).toBe("true")
    expect(revoke.getAttribute("data-denied")).toBe("needs platform-admin")

    fireEvent.click(revoke)
    expect(screen.queryByText("Revoke this grant?")).toBeNull()
  })
})
