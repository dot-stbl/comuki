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
import { ConnectSourcePage } from "@/domains/sources/pages/connect-source-page"
import {
  readSeedSources,
  resetSeedSources,
} from "@/shared/api/mock/sources.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* jsdom lays nothing out, and the shell under test depends on that: the rail is
   a resizable panel and without a measured port it has no layout to restore. */
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
     0`, and every test here mounts the shell inside the same document. Nothing
     is ever read back. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetSeedSources()
})

/* The rail links to every product screen, so a memory router that does not know
   those paths cannot render the shell at all. This is the product's own path
   list plus the three the section now has. */
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

  const create = createRoute({
    getParentRoute: () => rootRoute,
    path: "/sources/new",
    component: ConnectSourcePage,
  })

  const detail = createRoute({
    getParentRoute: () => rootRoute,
    path: "/sources/$sourceId",
    component: blank,
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    sources,
    create,
    detail,
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

const control = (testId: string) =>
  document.querySelector(`[data-test="${testId}"]`) as HTMLElement

const fill = (testId: string, value: string) =>
  fireEvent.change(control(testId), { target: { value } })

/** A complete github draft, env-var name + mock-mode credential. */
function fillDraft(secret = "ghp-not-a-real-token-0001") {
  fill("connect-name", "here/web-app")
  fill("connect-account", "svc-bot")
  fill("connect-secret-env", "COMUKI_GITHUB_TOKEN")
  fill("connect-mock-secret", secret)
}

/** Press test and wait for the provider to answer, either way. */
async function probe() {
  fireEvent.click(control("connect-test"))
  await waitFor(() => expect(control("probe-result")).not.toBeNull(), {
    timeout: 3000,
  })
}

describe("the connect form has a page, so it has a way back", () => {
  it("puts the list one click behind it, as a real link", async () => {
    mount(["/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })

    const crumb = screen.getByRole("link", { name: "sources" })
    expect(crumb.getAttribute("href")).toBe("/sources")
  })

  it("returns the operator to where they pressed connect, filters and all", async () => {
    // Arrived from the list, narrowed to a project — which is the ordinary way
    // in, because `/sources?q=` is how a project page hands its sources over.
    const router = mount(["/sources?q=plexor", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    // Back through history rather than a fresh navigation: a `navigate` would
    // have dropped the filter the operator left the list wearing.
    await waitFor(() => expect(here(router)).toBe("/sources?q=plexor"))
  })

  it("sends somebody who typed the url to the list instead", async () => {
    // Nothing behind this page — a bookmark, a pasted link, a fresh tab.
    const router = mount(["/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/sources"))
  })
})

describe("submitting lands on the thing it just made", () => {
  it("goes to the new connection's own page, where its watch is", async () => {
    const router = mount(["/sources", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fillDraft()
    await probe()

    fireEvent.click(screen.getByRole("button", { name: "Save connection" }))

    // The id is the store's to mint, so the assertion is the *shape* of the
    // destination rather than a string this test invented.
    await waitFor(
      () => expect(here(router)).toMatch(/^\/sources\/src_github_/),
      { timeout: 3000 }
    )

    const created = readSeedSources().connections.find(
      (entry) => entry.name === "here/web-app"
    )
    expect(created).toBeTruthy()
    // It arrives with its watch off: admitting tickets is a separate decision,
    // taken on the page it just landed on.
    expect(created?.watch?.enabled).toBe(false)
    // And what survives of the credential is a date.
    expect(JSON.stringify(created)).not.toContain("ghp-not-a-real-token-0001")
    expect(created?.secretStoredAt).toBe("just now")
  })

  it("replaces the form in history, so back does not return to it", async () => {
    const router = mount(["/sources", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fillDraft()
    await probe()
    fireEvent.click(screen.getByRole("button", { name: "Save connection" }))

    await waitFor(
      () => expect(here(router)).toMatch(/^\/sources\/src_github_/),
      { timeout: 3000 }
    )

    router.history.back()
    // A form that has already been submitted is not a place to go back to.
    await waitFor(() => expect(here(router)).toBe("/sources"))
  })

  it("keeps the save shut until the provider has actually answered", async () => {
    mount(["/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fillDraft("short")

    const save = screen.getByRole("button", { name: "Save connection" })
    expect(save.hasAttribute("disabled")).toBe(true)

    // A refusal is an answer and it is not a yes.
    await probe()
    expect(control("probe-result").getAttribute("data-tone")).toBe("bad")
    expect(save.hasAttribute("disabled")).toBe(true)

    // A good credential opens it — and then an edit shuts it again, because a
    // credential that worked before the account was changed is not evidence
    // about the account that is there now.
    fill("connect-mock-secret", "ghp-not-a-real-token-0001")
    await probe()
    await waitFor(() => expect(save.hasAttribute("disabled")).toBe(false))

    fill("connect-account", "another-bot")
    expect(control("probe-result")).toBeNull()
    expect(control("probe-pending")).not.toBeNull()
    expect(save.hasAttribute("disabled")).toBe(true)
  })
})

describe("leaving a half-filled form", () => {
  it("asks, because the page has a dozen ways out that a dialog did not", async () => {
    const router = mount(["/sources", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fill("connect-name", "here/web-app")

    fireEvent.click(screen.getByRole("link", { name: "sources" }))

    await screen.findByText("Leave without connecting the source?")
    expect(here(router)).toBe("/sources/new")
  })

  it("keeps every field when the answer is to stay", async () => {
    const router = mount(["/sources", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fill("connect-name", "here/web-app")
    fireEvent.click(screen.getByRole("link", { name: "sources" }))
    await screen.findByText("Leave without connecting the source?")

    fireEvent.click(screen.getByRole("button", { name: "Keep editing" }))

    await waitFor(() =>
      expect(
        screen.queryByText("Leave without connecting the source?")
      ).toBeNull()
    )
    expect(here(router)).toBe("/sources/new")
    expect((control("connect-name") as HTMLInputElement).value).toBe(
      "here/web-app"
    )
  })

  it("leaves when the answer is discard", async () => {
    const router = mount(["/sources", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fill("connect-name", "here/web-app")
    fireEvent.click(screen.getByRole("link", { name: "sources" }))
    await screen.findByText("Leave without connecting the source?")

    fireEvent.click(screen.getByRole("button", { name: "Discard" }))

    await waitFor(() => expect(here(router)).toBe("/sources"))
  })

  it("never asks about a departure that was the point", async () => {
    const router = mount(["/sources", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fill("connect-name", "here/web-app")

    // Cancel *is* the answer to "do you want to drop this". Asking again would
    // be asking the same question twice and calling it safety.
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/sources"))
    expect(
      screen.queryByText("Leave without connecting the source?")
    ).toBeNull()
  })

  it("never asks after a save that succeeded", async () => {
    const router = mount(["/sources", "/sources/new"])

    await screen.findByRole("heading", { name: "Connect a source" })
    fillDraft()
    await probe()
    fireEvent.click(screen.getByRole("button", { name: "Save connection" }))

    await waitFor(
      () => expect(here(router)).toMatch(/^\/sources\/src_github_/),
      { timeout: 3000 }
    )
    expect(
      screen.queryByText("Leave without connecting the source?")
    ).toBeNull()
  })
})
