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
import { CreateTicketPage } from "@/domains/sources/pages/create-ticket-page"
import {
  readSeedSources,
  resetSeedSources,
} from "@/shared/api/mock/sources.store"
import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import { SessionProvider, type Role } from "@/shared/session"

/* The dialog that used to open over the connections list is a page now, and
   every decision it held came with it: the four fields, the switch that is a
   switch because the two acts differ by one bit, and the permission — which is
   `inbox.take` and emphatically not `sources.edit`.
 *
 * It is the one form in this section that did *not* fold into the source's own
 * page, because it creates a different entity with a different lifetime, taken
 * by a different person.
 */

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
  resetSeedSources()
})

const RAIL_PATHS = [
  "/",
  "/chat",
  "/tasks",
  "/runs",
  "/queue",
  "/approvals",
  "/cost",
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

/** The seeded native intake on `p_comuki`, which is where a ticket can go. */
const NATIVE = "src_native_comuki"

interface Search {
  q?: string
}

function buildRouteTree() {
  const rootRoute = createRootRoute()
  const blank = () => null

  const sources = createRoute({
    getParentRoute: () => rootRoute,
    path: "/sources",
    validateSearch: (search: Record<string, unknown>): Search =>
      typeof search.q === "string" && search.q ? { q: search.q } : {},
    component: blank,
  })

  const detail = createRoute({
    getParentRoute: () => rootRoute,
    path: "/sources/$sourceId",
    component: blank,
  })

  const ticket = createRoute({
    getParentRoute: () => rootRoute,
    path: "/sources/$sourceId/ticket/new",
    component: function TicketRoute() {
      const { sourceId } = ticket.useParams()
      return <CreateTicketPage sourceId={sourceId} />
    },
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    sources,
    detail,
    ticket,
  ])
}

function mount(
  entries: string[],
  roles: Role[] = ["platform-admin"],
  projectRoles: Record<string, Role[]> = {}
) {
  const router = createRouter({
    routeTree: buildRouteTree(),
    history: createMemoryHistory({ initialEntries: entries }),
  })

  /* The *seeded* projects rather than a pair invented for the test: the
     connection under test lives on `p_comuki`, and the whole point of the
     denial sentence is that it names the project by the key the operator calls
     it — which only exists if the session can see it. */
  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <SessionProvider
        user={{
          id: "u_test",
          name: "Test User",
          email: "test@comuki.local",
          platformRoles: roles,
          projectRoles,
        }}
        projects={PROJECTS_SEED}
      >
        <QueryClientProvider
          client={
            new QueryClient({ defaultOptions: { queries: { retry: false } } })
          }
        >
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </QueryClientProvider>
      </SessionProvider>
    </ThemeProvider>
  )

  return router
}

const here = (router: ReturnType<typeof mount>) =>
  `${router.state.location.pathname}${router.state.location.searchStr}`

const control = (testId: string) =>
  document.querySelector(`[data-test="${testId}"]`) as HTMLElement

const fill = (testId: string, value: string) =>
  fireEvent.change(control(testId), { target: { value } })

const submitButton = () => control("form-submit") as HTMLButtonElement

async function open(
  roles: Role[] = ["platform-admin"],
  projectRoles: Record<string, Role[]> = {},
  entries: string[] = ["/sources/" + NATIVE, `/sources/${NATIVE}/ticket/new`]
) {
  const router = mount(entries, roles, projectRoles)
  await screen.findByLabelText("title")
  return router
}

describe("filing a ticket in the product's own intake", () => {
  it("will not create one with no title", async () => {
    await open()

    // Incomplete, not forbidden — so `disabled`, and the two must not look
    // alike.
    expect(submitButton().hasAttribute("disabled")).toBe(true)
    expect(submitButton().hasAttribute("aria-disabled")).toBe(false)
  })

  it("files a ticket with its labels already split", async () => {
    const before = readSeedSources().tickets.length
    await open()

    fill("ticket-title", "  ledger-core prints a float tail  ")
    fill("ticket-body", "formatting, not storage")
    fill("ticket-labels", "ledger-core, reporting")
    fireEvent.click(submitButton())

    await waitFor(
      () => expect(readSeedSources().tickets).toHaveLength(before + 1),
      { timeout: 3000 }
    )

    const filed = readSeedSources().tickets[0]
    expect(filed.projectId).toBe("p_comuki")
    expect(filed.title).toBe("ledger-core prints a float tail")
    expect(filed.body).toBe("formatting, not storage")
    // A comma-separated list is a fact about a text box. The filter expression
    // one screen over is left whole precisely because nobody has decided what
    // *its* separators mean — the hint on this field says which is which.
    expect(filed.labels).toEqual(["ledger-core", "reporting"])
    expect(filed.straightToWork).toBe(false)
  })

  it("says what the button will do when work starts on save", async () => {
    await open()

    fill("ticket-title", "a bug")
    expect(submitButton().textContent).toBe("Create ticket")

    fireEvent.click(control("ticket-straight-to-work"))

    // One bit of difference, said in the button rather than left to be inferred
    // from a switch three fields up.
    expect(submitButton().textContent).toBe("Create and start")
    fireEvent.click(submitButton())

    await waitFor(
      () => expect(readSeedSources().tickets[0].straightToWork).toBe(true),
      { timeout: 3000 }
    )
  })

  it("says the ticket has no tracker behind it", async () => {
    await open()
    expect(screen.getByText(/there is nowhere to sync it back to/)).toBeTruthy()
  })

  it("says these labels are not a filter expression", async () => {
    await open()

    // The box directly above this one on a source's own page is the filter
    // expression, which is never parsed. Two boxes of comma-ish text, one with
    // a meaning and one deliberately without, is the pair that needs saying.
    expect(
      screen.getByText(/These are the ticket's own labels — not a filter/)
    ).toBeTruthy()
  })
})

describe("putting work into intake is a member's act", () => {
  it("keeps the create visible for a role that may not use it", async () => {
    const before = readSeedSources().tickets.length
    // A viewer on the connection's project: may read the section, may not put
    // anything into it.
    await open(["viewer"], { p_comuki: ["viewer"] })

    fill("ticket-title", "a bug")

    expect(submitButton().getAttribute("aria-disabled")).toBe("true")
    expect(submitButton().hasAttribute("disabled")).toBe(false)

    // The sentence names `member` first, which is the whole assertion: this
    // form answers to `inbox.take`, not to `sources.edit`. Requiring a project
    // administrator to write down a bug would be the wrong shape even though
    // this section is an administrator's.
    expect(submitButton().getAttribute("title")).toBe(
      "needs member, approver, project-admin, operator or platform-admin on comuki"
    )
    expect(submitButton().getAttribute("title")).not.toContain(
      "needs project-admin or platform-admin"
    )

    fireEvent.click(submitButton())
    expect(readSeedSources().tickets).toHaveLength(before)
  })

  it("opens for a member who could not touch the connection at all", async () => {
    // `member` grants `inbox.take` and refuses `sources.edit`. The form is
    // live, which is the point of gating it on the act rather than the screen.
    await open(["viewer"], { p_comuki: ["member"] })

    fill("ticket-title", "a bug")
    expect(submitButton().hasAttribute("aria-disabled")).toBe(false)
    expect(submitButton().hasAttribute("disabled")).toBe(false)
  })
})

describe("the page hangs off the source it files into", () => {
  it("names the connection in the crumbs and links back to its page", async () => {
    await open()

    const crumb = screen.getByRole("link", { name: "native intake" })
    expect(crumb.getAttribute("href")).toBe(`/sources/${NATIVE}`)
    expect(
      screen.getByRole("link", { name: "sources" }).getAttribute("href")
    ).toBe("/sources")
  })

  it("returns through history when there is history", async () => {
    const router = await open()

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))
    await waitFor(() => expect(here(router)).toBe(`/sources/${NATIVE}`))
  })

  it("returns to the source's own page when there is none", async () => {
    const router = await open(["platform-admin"], {}, [
      `/sources/${NATIVE}/ticket/new`,
    ])

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))
    await waitFor(() => expect(here(router)).toBe(`/sources/${NATIVE}`))
  })

  it("lands back on the source after a ticket is filed", async () => {
    const router = await open()

    fill("ticket-title", "a bug")
    fireEvent.click(submitButton())

    await waitFor(() => expect(here(router)).toBe(`/sources/${NATIVE}`), {
      timeout: 3000,
    })

    // Replaced rather than pushed: a form that has already been submitted is
    // not somewhere back should return to.
    router.history.back()
    await waitFor(() => expect(here(router)).toBe(`/sources/${NATIVE}`))
  })

  it("says so when the source is gone rather than offering a form", async () => {
    await mount([`/sources/src_vanished/ticket/new`])

    await screen.findByText(/No connection on this platform has the id/)
    // The id is named, because the operator is going to compare it with
    // whatever they pasted.
    expect(control("ticket-source-gone").textContent).toContain("src_vanished")
    expect(screen.queryByLabelText("title")).toBeNull()
    expect(screen.getByRole("link", { name: "Back to sources" })).toBeTruthy()
  })
})

describe("leaving a half-written ticket", () => {
  it("asks before dropping what was typed", async () => {
    const router = await open()

    fill("ticket-title", "a bug")
    fireEvent.click(screen.getByRole("link", { name: "sources" }))

    await screen.findByText("Leave without filing the ticket?")
    expect(here(router)).toBe(`/sources/${NATIVE}/ticket/new`)

    fireEvent.click(screen.getByRole("button", { name: "Discard" }))
    await waitFor(() => expect(here(router)).toBe("/sources"))
  })

  it("never asks about a departure that was the point", async () => {
    const router = await open()

    fill("ticket-title", "a bug")
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe(`/sources/${NATIVE}`))
    expect(screen.queryByText("Leave without filing the ticket?")).toBeNull()
  })
})
