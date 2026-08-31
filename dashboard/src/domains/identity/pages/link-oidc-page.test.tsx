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
import { LinkOidcPage } from "@/domains/identity/pages/link-oidc-page"
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

  /* jsdom measures nothing, so the shell rail's persisted split layout is
     meaningless and restoring it on a second mount throws. */
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
  "/identity",
  "/compute",
  "/models",
  "/observability",
  "/components",
]

/* Arrived at from the section, which is the ordinary way in and the history
   the cancel case reads. `mountAt` is the same harness with the history spelled
   out, for the case where there is none. */
function mount(userId: string, roles: Role[] = ["platform-admin"]) {
  return mountAt(["/identity", `/identity/users/${userId}/link`], roles)
}

function mountAt(entries: string[], roles: Role[] = ["platform-admin"]) {
  const rootRoute = createRootRoute()
  const blank = () => null
  const link = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity/users/$userId/link",
    component: function LinkRoute() {
      const { userId: id } = link.useParams()
      return <LinkOidcPage userId={id} />
    },
  })
  const routeTree = rootRoute.addChildren([
    ...[...RAIL_PATHS, "/identity/users/$userId"].map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    link,
  ])

  const router = createRouter({
    routeTree,
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

const here = (router: ReturnType<typeof mountAt>) =>
  `${router.state.location.pathname}${router.state.location.searchStr}`

describe("editing an account that already exists", () => {
  it("writes the subject and returns to the account it belongs to", async () => {
    // `u_nadia` is the seeded account with no provider subject.
    const router = mount("u_nadia")

    await screen.findByLabelText("subject")
    // The page says whose account this is — the URL names an id, and an id is
    // not something an administrator recognises. It now says it twice, in the
    // summary and in the crumb that leads back to them.
    expect(screen.getByText(/subject for nadia@plexor\.dev/)).toBeTruthy()
    // And the person is this page's parent, one crumb behind it.
    expect(
      screen
        .getByRole("link", { name: "nadia@plexor.dev" })
        .getAttribute("href")
    ).toBe("/identity/users/u_nadia")

    fireEvent.change(screen.getByLabelText("subject"), {
      target: { value: "oidc|plexor|9931" },
    })
    fireEvent.click(screen.getByRole("button", { name: "Link subject" }))

    await waitFor(() =>
      expect(
        listSeedUsers().find((user) => user.id === "u_nadia")?.oidcSubject
      ).toBe("oidc|plexor|9931")
    )
    // Back to the person, not to a list narrowed to them: the operator came
    // from the account and the subject they just wrote is a fact about it, so
    // the confirmation is the record itself rather than a filtered table.
    await waitFor(() => expect(here(router)).toBe("/identity/users/u_nadia"))
  })

  it("refuses an empty subject and says which rule it broke", async () => {
    mount("u_nadia")

    await screen.findByLabelText("subject")
    fireEvent.click(screen.getByRole("button", { name: "Link subject" }))

    expect(screen.getByRole("alert").textContent).toContain(
      "a subject is required"
    )
    expect(
      listSeedUsers().find((user) => user.id === "u_nadia")?.oidcSubject
    ).toBeNull()
  })
})

/* A URL that names a subject can be stale, and both stale cases are ordinary:
   an id that no longer resolves, and an account somebody linked in the other
   tab while this one sat open. A dialog opened from a row could not reach
   either state; a page has to answer for both. */
describe("an address that no longer means what it did", () => {
  it("says so rather than rendering a form that would fail on submit", async () => {
    mount("u_gone")

    await screen.findByRole("heading", { name: "Link an oidc subject" })
    await screen.findByText(/No account on this platform has that id/)
    expect(screen.queryByLabelText("subject")).toBeNull()
    // No person to name and none to go back to, so the crumb path falls back
    // to the section rather than pointing at a page about to say the same
    // thing, and the way out is the section too.
    expect(screen.queryByRole("link", { name: /@/ })).toBeNull()
    expect(
      screen
        .getByRole("link", { name: "Back to identity" })
        .getAttribute("href")
    ).toBe("/identity")
  })

  it("refuses to overwrite a subject that is already written", async () => {
    // `u_rhea` arrives from the seed already linked.
    mount("u_rhea")

    await screen.findByRole("heading", { name: "Link an oidc subject" })
    await screen.findByText(/already linked to/)
    // Relinking is not an act this product has, so there is no form offering
    // one.
    expect(screen.queryByLabelText("subject")).toBeNull()
    // The way out is the person, because there is one — "back to identity"
    // would walk past the page this one belongs to.
    expect(
      screen
        .getByRole("link", { name: "Back to rhea@comuki.local" })
        .getAttribute("href")
    ).toBe("/identity/users/u_rhea")
  })
})

describe("leaving a half-filled form", () => {
  it("asks before dropping the subject that was typed", async () => {
    const router = mount("u_nadia")

    await screen.findByLabelText("subject")
    fireEvent.change(screen.getByLabelText("subject"), {
      target: { value: "oidc|plexor|9931" },
    })
    fireEvent.click(screen.getByRole("link", { name: "identity" }))

    await screen.findByText("Leave without linking the subject?")
    expect(here(router)).toBe("/identity/users/u_nadia/link")

    fireEvent.click(screen.getByRole("button", { name: "Discard" }))
    await waitFor(() => expect(here(router)).toBe("/identity"))
  })

  it("returns where the operator came from on cancel, without asking", async () => {
    const router = mount("u_nadia")

    await screen.findByLabelText("subject")
    fireEvent.change(screen.getByLabelText("subject"), {
      target: { value: "oidc|plexor|9931" },
    })
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    // History still wins where there is any: the screen behind may have been
    // narrowed, and a fresh navigation would drop the filter. Only the
    // fallback destination changed.
    await waitFor(() => expect(here(router)).toBe("/identity"))
    expect(screen.queryByText("Leave without linking the subject?")).toBeNull()
  })

  it("falls back to the person when there is no history to go back through", async () => {
    // A pasted link, a bookmark, a fresh tab. `mount` seeds two entries, so
    // this one builds its own history of exactly one.
    const router = mountAt(["/identity/users/u_nadia/link"])

    await screen.findByLabelText("subject")
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/identity/users/u_nadia"))
  })
})
