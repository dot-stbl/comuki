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
import { IdentityPage } from "@/domains/identity/pages/identity-page"
import { LinkOidcPage } from "@/domains/identity/pages/link-oidc-page"
import { UserDetailPage } from "@/domains/identity/pages/user-detail-page"
import type { IdentityTab } from "@/domains/identity/model/tabs"
import {
  listSeedUsers,
  resetSeedIdentity,
  revokeSeedRole,
} from "@/shared/api/mock/identity.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* jsdom lays nothing out, and both screens under test depend on that: the
   people list's table body is virtualized, and the shell's rail is a resizable
   panel. Without a measured port the rows never render and the panel has no
   layout to restore. */
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

  /* `SplitPane` persists the shell rail's divider position, and in jsdom the
     layout it saves was measured against nothing. Restoring that on the next
     mount makes `react-resizable-panels` throw `No layout data found for index
     0` — and every test here that navigates between two screens mounts the
     shell a second time inside the same document, so clearing the store
     between tests is not enough. Nothing is ever read back. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetSeedIdentity()
})

/* The rail links to every product screen, so a memory router that does not
   know those paths cannot render the shell at all. `/identity` is not in the
   list because this file gives it a real component. */
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

const TABS = ["users", "grants", "keys"]

function buildRouteTree() {
  const rootRoute = createRootRoute()
  const blank = () => null

  /* The real section, because two of the assertions here are about a hand-off
     landing on it: a link that carried a filter the destination could not
     receive would still have the right href and still be wrong. */
  const identity = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity",
    validateSearch: (search: Record<string, unknown>): Search => {
      const parsed: Search = {}
      if (typeof search.tab === "string" && TABS.includes(search.tab)) {
        parsed.tab = search.tab as IdentityTab
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

  const person = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity/users/$userId",
    component: function PersonRoute() {
      const { userId } = person.useParams()
      return <UserDetailPage userId={userId} />
    },
  })

  const link = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity/users/$userId/link",
    component: function LinkRoute() {
      const { userId } = link.useParams()
      return <LinkOidcPage userId={userId} />
    },
  })

  return rootRoute.addChildren([
    ...[
      ...RAIL_PATHS,
      "/identity/users/new",
      "/identity/grants/new",
      "/identity/keys/new",
    ].map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    identity,
    person,
    link,
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

/* The product marks its own landmarks with `data-test`, not `data-testid`. */
const find = (selector: string) =>
  document.querySelector<HTMLElement>(`[data-test="${selector}"]`)

describe("the way in from the list", () => {
  it("makes the address the link, and leaves the row alone", async () => {
    mount(["/identity"])

    const address = await screen.findByRole("link", {
      name: "nadia@plexor.dev",
    })
    expect(address.getAttribute("href")).toBe("/identity/users/u_nadia")

    // The identifier cell is the link and the row is not: a row-wide target
    // would swallow the two buttons at the end of it, and somebody reaching
    // for "disable" would land on a page instead.
    const disable = screen.getByRole("button", {
      name: "Disable nadia@plexor.dev",
    })
    expect(address.contains(disable)).toBe(false)
    expect(disable.closest("a")).toBeNull()
  })

  it("opens the person it names", async () => {
    const router = mount(["/identity"])

    fireEvent.click(
      await screen.findByRole("link", { name: "nadia@plexor.dev" })
    )

    await waitFor(() => expect(here(router)).toBe("/identity/users/u_nadia"))
    await screen.findByRole("heading", { name: "Nadia Ferrer" })
  })
})

describe("an address that no longer means what it did", () => {
  it("names the account that is missing rather than saying not found", async () => {
    mount(["/identity/users/u_gone"])

    await screen.findByText("No account with that id")
    // The id off the URL, said out loud: a stale tab and an old link are the
    // ordinary ways to arrive here, and neither is diagnosable from "not
    // found".
    expect(screen.getByText("u_gone")).toBeTruthy()
    expect(find("user-not-found")).not.toBeNull()
    expect(
      screen
        .getByRole("link", { name: "Back to identity" })
        .getAttribute("href")
    ).toBe("/identity")
  })

  it("shows no account facts and no act at all", async () => {
    mount(["/identity/users/u_gone"])

    await screen.findByText("No account with that id")
    expect(find("user-account")).toBeNull()
    expect(find("user-toggle-disabled")).toBeNull()
  })
})

describe("what this person holds", () => {
  it("lists each role and the scope it holds on", async () => {
    mount(["/identity/users/u_nadia"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })

    // Two projects and no platform standing — the seeded awkward case.
    expect(screen.getByText("approver")).toBeTruthy()
    expect(screen.getByText("comuki")).toBeTruthy()
    expect(screen.getByText("project-admin")).toBeTruthy()
    expect(screen.getByText("atlas")).toBeTruthy()
    expect(document.querySelectorAll('[data-test="user-grant"]').length).toBe(2)
    expect(screen.getByText("2 held")).toBeTruthy()
  })

  it("says so in words when they hold nothing anywhere", async () => {
    // The seed has no such account, so one is made: Inés holds exactly one
    // viewer grant on atlas, and taking it away is the state this region is
    // written for — common, quiet, and not an error.
    revokeSeedRole("g_ines_atlas")
    mount(["/identity/users/u_ines"])

    await screen.findByRole("heading", { name: "Inés Moreau" })
    expect(find("user-holds-nothing")).not.toBeNull()
    expect(screen.getByText(/holds nothing/)).toBeTruthy()
    expect(document.querySelectorAll('[data-test="user-grant"]').length).toBe(0)
    expect(screen.getByText("0 held")).toBeTruthy()
  })

  it("reads a grant on a disabled account as inert, not as absent", async () => {
    // Tomas is switched off and still carries a live platform grant. Disabling
    // somebody and un-granting them are different acts, and the page has to
    // show both facts at once.
    mount(["/identity/users/u_tomas"])

    await screen.findByRole("heading", { name: "Tomas Lindqvist" })
    const grant = find("user-grant")
    expect(document.querySelectorAll('[data-test="user-grant"]').length).toBe(1)
    // Scoped to the row: `platform` is also a crumb, and the two are different
    // facts that happen to share a word.
    expect(grant?.textContent).toContain("operator")
    expect(grant?.textContent).toContain("platform")
    expect(screen.getByText("inert while the account is disabled")).toBeTruthy()
  })
})

describe("its own facts", () => {
  it("says never rather than leaving a blank where a date would be", async () => {
    mount(["/identity/users/u_ines"])

    await screen.findByRole("heading", { name: "Inés Moreau" })
    expect(find("user-last-seen")?.textContent).toBe("never")
    expect(find("user-status")?.textContent).toBe("invited")
  })

  it("offers the link act to an account with no subject", async () => {
    mount(["/identity/users/u_nadia"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })
    // Local only is not broken: OIDC says who you are, and linking is a
    // separate act from existing here. So the fact carries the act.
    expect(screen.getByText("local only")).toBeTruthy()
    expect(
      screen.getByRole("button", {
        name: "Link an oidc subject to nadia@plexor.dev",
      })
    ).toBeTruthy()
  })

  it("offers nothing to an account that already has one", async () => {
    mount(["/identity/users/u_rhea"])

    await screen.findByRole("heading", { name: "Rhea Okafor" })
    // A subject is written once. Relinking is not an act this product has, so
    // there is no control here that would perform one.
    expect(find("user-subject")?.textContent).toBe("oidc|comuki|4f21ba9c")
    expect(
      screen.queryByRole("button", {
        name: "Link an oidc subject to rhea@comuki.local",
      })
    ).toBeNull()
  })
})

describe("switching the account off", () => {
  it("asks first, in the words the list already uses", async () => {
    mount(["/identity/users/u_rhea"])

    await screen.findByRole("heading", { name: "Rhea Okafor" })
    fireEvent.click(
      screen.getByRole("button", { name: "Disable rhea@comuki.local" })
    )

    // One spelling of the sentence, shared with `UsersPanel` — including the
    // promise that makes disabling safe to answer yes to.
    expect(screen.getByText("Disable this account?")).toBeTruthy()
    expect(screen.getByText(/Their grants stay as they are/)).toBeTruthy()
    expect(listSeedUsers().find((user) => user.id === "u_rhea")?.status).toBe(
      "active"
    )
  })

  it("disables on confirmation", async () => {
    mount(["/identity/users/u_rhea"])

    await screen.findByRole("heading", { name: "Rhea Okafor" })
    fireEvent.click(
      screen.getByRole("button", { name: "Disable rhea@comuki.local" })
    )
    fireEvent.click(screen.getByRole("button", { name: "Disable" }))

    await waitFor(() =>
      expect(listSeedUsers().find((user) => user.id === "u_rhea")?.status).toBe(
        "disabled"
      )
    )
    // And the page it was performed on says so, rather than needing a reload.
    await waitFor(() =>
      expect(find("user-status")?.textContent).toBe("disabled")
    )
  })

  it("turns one back on without asking, because that is not destructive", async () => {
    mount(["/identity/users/u_tomas"])

    await screen.findByRole("heading", { name: "Tomas Lindqvist" })
    fireEvent.click(
      screen.getByRole("button", { name: "Enable tomas@comuki.local" })
    )

    expect(screen.queryByText("Disable this account?")).toBeNull()
    await waitFor(() =>
      expect(
        listSeedUsers().find((user) => user.id === "u_tomas")?.status
      ).toBe("active")
    )
    // The grant that was inert a moment ago is in force again, untouched.
    await waitFor(() =>
      expect(
        screen.queryByText("inert while the account is disabled")
      ).toBeNull()
    )
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps every act in the document and names what it needs", async () => {
    // `operator` is platform ops and holds no `identity.manage` — the whole
    // point of the role. The acts stay where they are and explain themselves;
    // a shorter screen teaches nobody what to ask for.
    mount(["/identity/users/u_nadia"], ["operator"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })

    const disable = screen.getByRole("button", {
      name: "Disable nadia@plexor.dev",
    })
    expect(disable.getAttribute("aria-disabled")).toBe("true")
    expect(disable.getAttribute("data-denied")).toBe("needs platform-admin")
    expect(disable.hasAttribute("disabled")).toBe(false)

    fireEvent.click(disable)
    expect(screen.queryByText("Disable this account?")).toBeNull()

    const link = screen.getByRole("button", {
      name: "Link an oidc subject to nadia@plexor.dev",
    })
    expect(link.getAttribute("data-denied")).toBe("needs platform-admin")

    // The two hand-offs that are acts rather than readings are buttons here,
    // not anchors: an anchor has no way to refuse and explain itself.
    expect(find("user-grant-new")?.getAttribute("data-denied")).toBe(
      "needs platform-admin"
    )
    expect(find("user-key-new")?.getAttribute("data-denied")).toBe(
      "needs platform-admin"
    )
  })

  it("leaves the readings reachable, because reading is not the act", async () => {
    mount(["/identity/users/u_nadia"], ["operator"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })
    expect(find("user-grants-all")?.tagName).toBe("A")
    expect(find("user-keys-all")?.tagName).toBe("A")
  })
})

describe("the hand-offs carry their filters", () => {
  it("sends the roles region to the assignments list, narrowed to this person", async () => {
    mount(["/identity/users/u_nadia"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })

    const href = find("user-grants-all")?.getAttribute("href") ?? ""
    expect(href.startsWith("/identity?")).toBe(true)
    expect(href).toContain("tab=grants")
    // The grants list matches on `subjectLabel`, which *is* the address — so
    // the filter lands rather than arriving on an empty table.
    expect(href).toContain(encodeURIComponent("nadia@plexor.dev"))

    expect(find("user-grant-new")?.getAttribute("href")).toBe(
      "/identity/grants/new"
    )
  })

  it("sends the keys region to the keys list and to the create form", async () => {
    mount(["/identity/users/u_nadia"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })

    const href = find("user-keys-all")?.getAttribute("href") ?? ""
    expect(href.startsWith("/identity?")).toBe(true)
    expect(href).toContain("tab=keys")
    // No address on this one, deliberately: a key belongs to no account, so a
    // key list narrowed to a person would be narrowed by a relation that does
    // not exist.
    expect(href).not.toContain("q=")

    expect(find("user-key-new")?.getAttribute("href")).toBe(
      "/identity/keys/new"
    )
  })

  it("counts the keys in force rather than inventing this person's", async () => {
    mount(["/identity/users/u_nadia"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })
    // Three active, one revoked, and the revoked one stays out of the figure
    // while staying in the list as the audit trail.
    expect(screen.getByText("3 in force")).toBeTruthy()
    expect(find("user-keys-note")?.textContent).toContain(
      "belongs to no account"
    )
  })

  it("actually lands on a list that can receive the filter", async () => {
    const router = mount(["/identity/users/u_nadia"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })
    fireEvent.click(
      screen.getByRole("link", { name: "every assignment for this person" })
    )

    await waitFor(() =>
      expect(here(router)).toBe("/identity?tab=grants&q=nadia%40plexor.dev")
    )
    // And the narrowing is visible: the filter the list arrived with is in the
    // toolbar, one click from being cleared.
    await waitFor(() =>
      expect(
        (screen.getByPlaceholderText(/filter subject/i) as HTMLInputElement)
          .value
      ).toBe("nadia@plexor.dev")
    )
  })
})

describe("the crumbs and the return path agree with the parent", () => {
  it("puts the person one crumb behind the link form, as a real link", async () => {
    mount(["/identity/users/u_nadia/link"])

    await screen.findByLabelText("subject")
    const crumb = screen.getByRole("link", { name: "nadia@plexor.dev" })
    expect(crumb.getAttribute("href")).toBe("/identity/users/u_nadia")
    // The section is still one step further back.
    expect(
      screen.getByRole("link", { name: "identity" }).getAttribute("href")
    ).toBe("/identity")
  })

  it("names the kind of thing when it does not yet know which one", async () => {
    // An id that resolves to nothing has no person to name, so the crumb is
    // left out rather than pointing at a page about to say the same thing.
    mount(["/identity/users/u_gone/link"])

    await screen.findByText(/No account on this platform has that id/)
    expect(screen.queryByRole("link", { name: /@/ })).toBeNull()
  })

  it("returns to the person on cancel when there is nothing behind", async () => {
    // A pasted link, a bookmark, a fresh tab: no history to go back through.
    const router = mount(["/identity/users/u_nadia/link"])

    await screen.findByLabelText("subject")
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/identity/users/u_nadia"))
  })

  it("goes from the person to the form and back again on cancel", async () => {
    const router = mount(["/identity/users/u_nadia"])

    await screen.findByRole("heading", { name: "Nadia Ferrer" })
    fireEvent.click(
      screen.getByRole("button", {
        name: "Link an oidc subject to nadia@plexor.dev",
      })
    )

    await waitFor(() =>
      expect(here(router)).toBe("/identity/users/u_nadia/link")
    )
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/identity/users/u_nadia"))
  })

  it("lands back on the person once the subject is written", async () => {
    const router = mount([
      "/identity/users/u_nadia",
      "/identity/users/u_nadia/link",
    ])

    await screen.findByLabelText("subject")
    fireEvent.change(screen.getByLabelText("subject"), {
      target: { value: "oidc|plexor|9931" },
    })
    fireEvent.click(screen.getByRole("button", { name: "Link subject" }))

    await waitFor(() => expect(here(router)).toBe("/identity/users/u_nadia"))
    // The confirmation is the fact itself, on the record it was written to.
    await waitFor(() =>
      expect(find("user-subject")?.textContent).toBe("oidc|plexor|9931")
    )
  })

  it("sends an already-linked account back to the person, not to the section", async () => {
    mount(["/identity/users/u_rhea/link"])

    await screen.findByText(/already linked to/)
    const back = screen.getByRole("link", { name: "Back to rhea@comuki.local" })
    expect(back.getAttribute("href")).toBe("/identity/users/u_rhea")
  })
})
