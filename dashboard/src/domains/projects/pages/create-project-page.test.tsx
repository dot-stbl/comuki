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
import { CreateProjectPage } from "@/domains/projects/pages/create-project-page"
import { ProjectsPage } from "@/domains/projects/pages/projects-page"
import { resetSeedProjects } from "@/shared/api/mock/projects.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* jsdom lays nothing out, and both screens under test depend on that: the
   registry's table body is virtualized, and the shell's rail is a resizable
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
     0` — and every test here that navigates from the form to the registry
     mounts the shell a second time inside the same document, so clearing the
     store between tests is not enough. Nothing is ever read back. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetSeedProjects()
})

/* The rail links to every product screen, so a memory router that does not
   know those paths cannot render the shell at all. This is the product's own
   path list plus the one route under test. */
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

  const projects = createRoute({
    getParentRoute: () => rootRoute,
    path: "/projects",
    validateSearch: (search: Record<string, unknown>): Search =>
      typeof search.q === "string" && search.q ? { q: search.q } : {},
    component: function ProjectsRoute() {
      const { q } = projects.useSearch()
      return <ProjectsPage focus={q} />
    },
  })

  const create = createRoute({
    getParentRoute: () => rootRoute,
    path: "/projects/new",
    component: CreateProjectPage,
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    projects,
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

const here = (router: ReturnType<typeof mount>) =>
  `${router.state.location.pathname}${router.state.location.searchStr}`

describe("the form has a page, so it has a way back", () => {
  it("puts the registry one click behind it, as a real link", async () => {
    mount(["/projects/new"])

    await screen.findByRole("heading", { name: "New project" })

    const crumb = screen.getByRole("link", { name: "projects" })
    expect(crumb.getAttribute("href")).toBe("/projects")
  })

  it("returns the operator to where they pressed new, filters and all", async () => {
    // Arrived from the registry, which is the ordinary way in.
    const router = mount(["/projects?q=atlas", "/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    // Back through history rather than a fresh navigation: the registry the
    // operator left had a filter on it, and a `navigate` would have dropped it.
    await waitFor(() => expect(here(router)).toBe("/projects?q=atlas"))
  })

  it("sends somebody who typed the url to the registry instead", async () => {
    // Nothing behind this page — a bookmark, a pasted link, a fresh tab.
    const router = mount(["/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/projects"))
  })
})

describe("submitting lands on the thing it just created", () => {
  it("goes to the registry narrowed to the new slug", async () => {
    const router = mount(["/projects", "/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Orbital" },
    })
    fireEvent.click(screen.getByRole("button", { name: "Create project" }))

    await waitFor(() => expect(here(router)).toBe("/projects?q=orbital"))

    // And the narrowing is *visible*: the filter the list arrived with is
    // sitting in the toolbar, holding the slug, one click from being cleared.
    await waitFor(() =>
      expect(
        (screen.getByPlaceholderText(/filter slug/i) as HTMLInputElement).value
      ).toBe("orbital")
    )
    expect(screen.getByText("Orbital")).toBeTruthy()
  })

  it("replaces the form in history, so back does not return to it", async () => {
    const router = mount(["/projects", "/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Orbital" },
    })
    fireEvent.click(screen.getByRole("button", { name: "Create project" }))

    await waitFor(() => expect(here(router)).toBe("/projects?q=orbital"))

    router.history.back()
    // A form that has already been submitted is not a place to go back to.
    await waitFor(() => expect(here(router)).toBe("/projects"))
  })
})

describe("leaving a half-filled form", () => {
  it("asks, because the page has a dozen ways out that a dialog did not", async () => {
    const router = mount(["/projects", "/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Orbital" },
    })

    fireEvent.click(screen.getByRole("link", { name: "projects" }))

    await screen.findByText("Leave without creating the project?")
    expect(here(router)).toBe("/projects/new")
  })

  it("keeps every field when the answer is to stay", async () => {
    const router = mount(["/projects", "/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Orbital" },
    })
    fireEvent.click(screen.getByRole("link", { name: "projects" }))
    await screen.findByText("Leave without creating the project?")

    fireEvent.click(screen.getByRole("button", { name: "Keep editing" }))

    await waitFor(() =>
      expect(
        screen.queryByText("Leave without creating the project?")
      ).toBeNull()
    )
    expect(here(router)).toBe("/projects/new")
    // The slug proposed from the name is still there too: staying means
    // staying, not starting again.
    expect((screen.getByLabelText("slug") as HTMLInputElement).value).toBe(
      "orbital"
    )
  })

  it("leaves when the answer is discard", async () => {
    const router = mount(["/projects", "/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Orbital" },
    })
    fireEvent.click(screen.getByRole("link", { name: "projects" }))
    await screen.findByText("Leave without creating the project?")

    fireEvent.click(screen.getByRole("button", { name: "Discard" }))

    await waitFor(() => expect(here(router)).toBe("/projects"))
  })

  it("never asks about a departure that was the point", async () => {
    const router = mount(["/projects", "/projects/new"])

    await screen.findByRole("heading", { name: "New project" })
    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Orbital" },
    })

    // Cancel *is* the answer to "do you want to drop this". Asking again would
    // be asking the same question twice and calling it safety.
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))

    await waitFor(() => expect(here(router)).toBe("/projects"))
    expect(screen.queryByText("Leave without creating the project?")).toBeNull()
  })
})
