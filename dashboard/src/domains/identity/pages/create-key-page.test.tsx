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
import { CreateKeyPage } from "@/domains/identity/pages/create-key-page"
import { isIdentityTab, type IdentityTab } from "@/domains/identity/model/tabs"
import { IdentityPage } from "@/domains/identity/pages/identity-page"
import {
  listSeedApiKeys,
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

  /* `SplitPane` persists the shell rail's divider, and in jsdom that layout was
     measured against nothing. Restoring it on a second mount inside the same
     document makes `react-resizable-panels` throw — and every test here that
     leaves the form for the list mounts the shell twice. */
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

  const create = createRoute({
    getParentRoute: () => rootRoute,
    path: "/identity/keys/new",
    component: CreateKeyPage,
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    identity,
    create,
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

const secretNode = () => document.querySelector('[data-test="secret-value"]')

const here = (router: ReturnType<typeof mount>) =>
  `${router.state.location.pathname}${router.state.location.searchStr}`

/** Everything the router is holding about where it is, as one string. */
const addressBar = (router: ReturnType<typeof mount>) =>
  `${router.history.location.href} ${JSON.stringify(
    router.history.location.state ?? {}
  )}`

async function createKey(router: ReturnType<typeof mount>, name: string) {
  await screen.findByRole("heading", { name: "New api key" })
  fireEvent.change(screen.getByLabelText("name"), { target: { value: name } })
  fireEvent.click(screen.getByRole("button", { name: "Create key" }))
  await waitFor(() => expect(secretNode()).not.toBeNull())
  expect(here(router)).toBe("/identity/keys/new")
  return secretNode()?.textContent ?? ""
}

describe("the secret is shown once, and the screen says so first", () => {
  it("warns before anything has been generated", async () => {
    mount(["/identity/keys/new"])

    await screen.findByRole("heading", { name: "New api key" })

    // The rule is above the button that makes the key, not beside the value
    // that has already appeared: a person who did not know cannot get it back.
    expect(
      screen.getByText(/The secret appears once, in a dialog over this page/)
    ).toBeTruthy()
    expect(secretNode()).toBeNull()
  })

  it("shows the plaintext once and never again", async () => {
    const router = mount(["/identity", "/identity/keys/new"])

    const plaintext = await createKey(router, "release-bot")
    expect(plaintext).toMatch(/^cmk_[0-9a-f]{4}_[0-9a-f]{32}$/)
    expect(document.body.textContent).toContain(plaintext)

    fireEvent.click(screen.getByRole("button", { name: "Done" }))

    await waitFor(() => expect(secretNode()).toBeNull())
    expect(document.body.textContent).not.toContain(plaintext)
  })

  it("stores only the prefix, so there is nothing to re-display", async () => {
    const router = mount(["/identity/keys/new"])

    const plaintext = await createKey(router, "release-bot")

    const stored = listSeedApiKeys().find((key) => key.name === "release-bot")
    expect(stored).toBeDefined()
    expect(plaintext.startsWith(stored?.prefix ?? "")).toBe(true)
    expect(JSON.stringify(stored)).not.toContain(plaintext)
  })

  it("confirms that it copied, because the value cannot be checked twice", async () => {
    const router = mount(["/identity/keys/new"])

    await createKey(router, "release-bot")

    fireEvent.click(screen.getByRole("button", { name: "copy" }))
    expect(screen.getByRole("button", { name: "copied" })).toBeTruthy()
  })
})

/* The whole reason the showing stayed a dialog while the form around it became
   a page. A page is a URL, and a URL can be reloaded, bookmarked, shared and
   walked back into — so a secret shown exactly once cannot live at one. These
   are the three ways an address could have leaked it. */
describe("no address renders the secret, so none can render it twice", () => {
  it("keeps it out of the path and the query string", async () => {
    const router = mount(["/identity/keys/new"])

    const plaintext = await createKey(router, "release-bot")

    // Still the plain form URL while the secret is on screen.
    expect(here(router)).toBe("/identity/keys/new")
    expect(addressBar(router)).not.toContain(plaintext)
  })

  it("keeps it out of the router's location state, which a reload restores", async () => {
    const router = mount(["/identity/keys/new"])

    const plaintext = await createKey(router, "release-bot")
    fireEvent.click(screen.getByRole("button", { name: "Done" }))

    await waitFor(() => expect(here(router)).toContain("/identity"))
    // Location state is `history.state`, which survives F5 and a back/forward
    // traversal. A secret carried there would be shown again on reload.
    expect(addressBar(router)).not.toContain(plaintext)
  })

  it("gives an empty form to anyone who comes back to the same url", async () => {
    const router = mount(["/identity", "/identity/keys/new"])

    const plaintext = await createKey(router, "release-bot")
    fireEvent.click(screen.getByRole("button", { name: "Done" }))
    await waitFor(() => expect(secretNode()).toBeNull())

    // The same address again — the closest thing a memory router has to a
    // reload, a bookmark or a pasted link.
    await router.navigate({ to: "/identity/keys/new" })

    await screen.findByRole("heading", { name: "New api key" })
    expect(secretNode()).toBeNull()
    expect(document.body.textContent).not.toContain(plaintext)
    expect((screen.getByLabelText("name") as HTMLInputElement).value).toBe("")
  })
})

describe("finishing lands on the key it just made", () => {
  it("goes to the keys list narrowed to the new prefix", async () => {
    const router = mount(["/identity", "/identity/keys/new"])

    const plaintext = await createKey(router, "release-bot")
    const prefix = plaintext.slice(0, "cmk_0000".length)

    fireEvent.click(screen.getByRole("button", { name: "Done" }))

    await waitFor(() =>
      expect(here(router)).toBe(`/identity?tab=keys&q=${prefix}`)
    )
    // And the narrowing is visible: the filter is in the toolbar holding the
    // prefix, one click from being cleared.
    await waitFor(() =>
      expect(
        (screen.getByPlaceholderText(/filter prefix/i) as HTMLInputElement)
          .value
      ).toBe(prefix)
    )
    expect(screen.getAllByText("release-bot").length).toBeGreaterThan(0)
  })

  it("replaces the form in history, so back does not return to it", async () => {
    const router = mount(["/identity", "/identity/keys/new"])

    await createKey(router, "release-bot")
    fireEvent.click(screen.getByRole("button", { name: "Done" }))
    await waitFor(() => expect(here(router)).toContain("tab=keys"))

    router.history.back()
    await waitFor(() => expect(here(router)).toBe("/identity"))
  })
})

describe("leaving before the key exists", () => {
  it("asks about a half-filled form", async () => {
    const router = mount(["/identity", "/identity/keys/new"])

    await screen.findByRole("heading", { name: "New api key" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "release-bot" },
    })
    fireEvent.click(screen.getByRole("link", { name: "identity" }))

    await screen.findByText("Leave without creating the key?")
    expect(here(router)).toBe("/identity/keys/new")
    // Nothing was generated, and the sentence says so — this is not a warning
    // about losing a secret, it is a warning about losing two fields.
    expect(
      screen.getByText(/No key has been made and no secret has been generated/)
    ).toBeTruthy()
  })

  it("stops asking once the key exists, because the act is done", async () => {
    const router = mount(["/identity", "/identity/keys/new"])

    await createKey(router, "release-bot")

    fireEvent.click(screen.getByRole("button", { name: "Done" }))

    // No question on the way out: the key was made, and the only thing left
    // to decide was whether the operator copied the value.
    await waitFor(() => expect(here(router)).toContain("tab=keys"))
    expect(screen.queryByText("Leave without creating the key?")).toBeNull()
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps the act in the document and names what it needs", async () => {
    mount(["/identity/keys/new"], ["operator"])

    await screen.findByRole("heading", { name: "New api key" })
    // Filled in first, so the only thing left standing between this shift and
    // a key is the denial — `disabled` here would mean "incomplete", which is
    // a different refusal with a different remedy.
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "release-bot" },
    })

    const create = screen.getByRole("button", { name: "Create key" })
    expect(create.getAttribute("aria-disabled")).toBe("true")
    expect(create.getAttribute("title")).toBe("needs platform-admin")
    // Not `disabled`: that would put the sentence out of reach of a pointer
    // and out of the tab order both.
    expect(create.hasAttribute("disabled")).toBe(false)

    fireEvent.click(create)
    expect(secretNode()).toBeNull()
  })
})
