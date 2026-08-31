import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { render, screen } from "@testing-library/react"
import { beforeAll, describe, expect, it } from "vitest"

import { buildProjectRows } from "@/domains/projects/model/activity"
import {
  createProjectColumns,
  getProjectId,
} from "@/domains/projects/ui/projects-columns"
import type { SeedProject } from "@/shared/api/mock/projects.seed"
import { DataTable } from "@/shared/ui"

/* jsdom lays nothing out and the table body is virtualized, so without a port
   depth the rows under test never render at all. */
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
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1200,
  })
})

const PROJECTS: SeedProject[] = [
  {
    id: "p_comuki",
    slug: "comuki",
    name: "Comuki platform",
    gitProfileRepo: "git@github.com:comuki/worker-profiles.git",
    createdAt: "2026-03-04",
  },
  {
    // Created two days ago: no runs, no spend, no repository.
    id: "p_vega",
    slug: "vega",
    name: "Vega",
    gitProfileRepo: null,
    createdAt: "2026-08-28",
  },
]

const rows = buildProjectRows(
  PROJECTS,
  [
    { projectId: "p_comuki", app: "web-app", status: "running" },
    { projectId: "p_comuki", app: "web-app", status: "success" },
  ],
  [{ app: "web-app", spend: 41.1 }]
)

/**
 * The registry, inside the least router that can hold it.
 *
 * The slug cell is a real anchor into `/projects/$projectId` — the identifier
 * cell is the way in, exactly as the run id is on the duty list — so the table
 * no longer renders outside a router at all. A memory router carrying the one
 * destination is the whole harness: nothing here navigates, and the cases
 * below are about what a row *says*.
 */
function mount() {
  const rootRoute = createRootRoute({
    component: function Registry() {
      return (
        <DataTable
          columns={createProjectColumns()}
          data={rows}
          getRowId={getProjectId}
          density="compact"
        />
      )
    },
  })

  const routeTree = rootRoute.addChildren([
    createRoute({
      getParentRoute: () => rootRoute,
      path: "/projects/$projectId",
      component: () => null,
    }),
  ])

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return render(<RouterProvider router={router as any} />)
}

/* The first query in each case is awaited because the router resolves its
   first match on a microtask — the assertions themselves are unchanged. */
describe("the registry row", () => {
  it("shows what a project is running and what it costs", async () => {
    mount()

    expect(await screen.findByText("comuki")).toBeTruthy()
    expect(screen.getByText("$41.10")).toBeTruthy()
  })

  it("degrades a project with nothing yet to dashes, not to blanks", async () => {
    mount()

    // A blank cell reads as a rendering fault; a dash reads as a fact.
    expect(await screen.findByText("vega")).toBeTruthy()
    expect(screen.getAllByText("—").length).toBe(3)
  })

  it("calls a missing repository what it actually is", async () => {
    mount()

    // Not missing — running on the platform's own profiles, which is a
    // legitimate way for a project to be configured.
    expect(await screen.findByText("platform defaults")).toBeTruthy()
  })

  it("shows the handle every other list in the product shows", async () => {
    mount()

    // The slug, not the display name, is the column head the operator scans.
    expect(await screen.findByText("slug")).toBeTruthy()
    expect(screen.getByText("Comuki platform")).toBeTruthy()
  })

  it("makes the identifier cell the way into the project's own screen", async () => {
    mount()

    // The run id on the duty list is spelled exactly this way, and for the
    // same reason: a destination is an anchor, so it can be opened in a tab,
    // copied, and read as a destination by anything that traverses links.
    await screen.findByText("comuki")
    const link = document.querySelector('[data-test="project-link"]')
    expect(link?.tagName).toBe("A")
    expect(link?.getAttribute("href")).toBe("/projects/p_comuki")
  })
})
