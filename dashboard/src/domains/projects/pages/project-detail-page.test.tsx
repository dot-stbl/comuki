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
import { ProjectDetailPage } from "@/domains/projects/pages/project-detail-page"
import { ProjectsPage } from "@/domains/projects/pages/projects-page"
import { resetSeedProjects } from "@/shared/api/mock/projects.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* jsdom lays nothing out, and both screens under test depend on that: the
   registry's table body is virtualized, and the shell's rail is a resizable
   panel. Without a measured port the rows never render and the panel has no
   layout to restore. Copied verbatim from `create-project-page.test.tsx`,
   which is the worked harness for exactly this pair. */
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
     mount makes `react-resizable-panels` throw. Nothing is ever read back. */
  vi.spyOn(Storage.prototype, "getItem").mockReturnValue(null)
})

beforeEach(() => {
  resetSeedProjects()
})

/* The rail links to every product screen, so a memory router that does not
   know those paths cannot render the shell at all. This is the product's own
   path list plus the routes under test. */
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
  "/identity/grants/new",
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

  const detail = createRoute({
    getParentRoute: () => rootRoute,
    path: "/projects/$projectId",
    component: function ProjectRoute() {
      // The route reads the param and hands it down, which is the arrangement
      // under test as much as the page is: the screen takes an id, not a
      // router, so it can also be mounted in a story.
      const { projectId } = detail.useParams()
      return <ProjectDetailPage projectId={projectId} />
    },
  })

  return rootRoute.addChildren([
    ...RAIL_PATHS.map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    ),
    projects,
    detail,
  ])
}

interface MountOptions {
  /** Platform roles. Empty is a real session — somebody with project grants. */
  roles?: Role[]
  /** Roles held on one project. Where most of the interesting cases live. */
  projectRoles?: Record<string, Role[]>
}

function mount(
  entries: string[],
  { roles = ["platform-admin"], projectRoles = {} }: MountOptions = {}
) {
  const router = createRouter({
    routeTree: buildRouteTree(),
    history: createMemoryHistory({ initialEntries: entries }),
  })

  render(
    <ThemeProvider defaultTheme="dark" storageKey="comuki-test-theme">
      <TestSession roles={roles} projectRoles={projectRoles}>
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

const at = (test: string) => document.querySelector(`[data-test="${test}"]`)

const handoff = (id: string) => at(`project-handoff-${id}`)

/* `p_atlas` is the seeded project with the most to say: forty runs in flight
   of fifty seen, a line in the cost report, four items waiting in the queue
   with no containers up for them, three source connections and three people
   holding a role on it. `p_vega` is two days old and has none of it. */

describe("a row in the registry opens the project it names", () => {
  it("makes the identifier cell the way in", async () => {
    const router = mount(["/projects"])

    await screen.findByText("Atlas")
    const link = [
      ...document.querySelectorAll('[data-test="project-link"]'),
    ].find((node) => node.textContent === "atlas")

    expect(link?.getAttribute("href")).toBe("/projects/p_atlas")
    fireEvent.click(link as Element)

    await waitFor(() => expect(here(router)).toBe("/projects/p_atlas"))
    await screen.findByRole("heading", { name: "Atlas" })
  })

  it("leaves the rest of the row inert", async () => {
    // The identifier cell is the link, never the row: a row-wide click target
    // swallows whatever an actions column puts beside it, and this list is one
    // act away from having one.
    const router = mount(["/projects"])

    const name = await screen.findByText("Atlas")
    fireEvent.click(name)
    // Two rows run on the platform's own profiles; either cell will do — the
    // point is that no cell but the handle is a destination.
    fireEvent.click(screen.getAllByText("platform defaults")[0] as Element)
    fireEvent.click(screen.getByText("$59.10"))

    expect(here(router)).toBe("/projects")
  })
})

describe("the page says what only it can say", () => {
  it("names the record, in the two voices it is written in", async () => {
    mount(["/projects/p_comuki"])

    await screen.findByRole("heading", { name: "Comuki platform" })
    const facts = at("project-facts")

    expect(facts?.textContent).toContain("comuki")
    expect(facts?.textContent).toContain(
      "git@github.com:comuki/worker-profiles.git"
    )
    expect(facts?.textContent).toContain("2026-03-04")
  })

  it("calls an absent repository what the registry already calls it", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })
    // Not missing — running on the platform's own profiles. Two spellings of
    // one fact is how two screens start disagreeing.
    expect(at("project-facts")?.textContent).toContain("platform defaults")
  })

  it("carries the handle and the day it was created in the summary", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })
    const header = at("page-header")

    expect(header?.textContent).toContain("atlas")
    expect(header?.textContent).toContain("2026-06-27")
  })
})

describe("everything else is a hand-off, with a count and a link", () => {
  it("sends each list the filter it narrows on", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })

    // The same `q` every screen in the product already spells, and the same
    // string the resolver builds — not a second set of parameters.
    expect(handoff("runs")?.getAttribute("href")).toBe("/runs?q=atlas")
    expect(handoff("queue")?.getAttribute("href")).toBe("/queue?q=atlas")
    expect(handoff("sources")?.getAttribute("href")).toBe("/sources?q=atlas")
  })

  it("counts what this project has over there, not what the platform has", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })

    // Both figures come off the registry row, which joins them from the run
    // list rather than storing them — there is no third place to be wrong in.
    expect(handoff("runs")?.textContent).toContain("40 in flight of 50 seen")
    // Four items queued and nothing up to claim them: atlas runs a
    // create-per-task pool, which is the reading this row exists for.
    await waitFor(() =>
      expect(handoff("queue")?.textContent).toContain(
        "4 work items · 0 workers up"
      )
    )
    await waitFor(() =>
      expect(handoff("sources")?.textContent).toContain("3 connections")
    )
    expect(handoff("cost")?.textContent).toContain("$59.10")
  })

  it("hands the work off rather than redrawing it here", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })
    await waitFor(() =>
      expect(handoff("queue")?.textContent).toContain("4 work items")
    )

    // The queue row counts four items and names none of them. A project page
    // that drew its own queue table would be a second claim queue, and the day
    // the two disagreed the operator would believe the one they were on.
    expect(screen.queryByText("wi_0002")).toBeNull()
    expect(
      screen.queryByText("описать новое окно хранения")
    ).toBeNull()
  })

  it("says out loud that the cost report behind it is not narrowed", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })

    expect(handoff("cost")?.getAttribute("href")).toBe("/cost")
    // There is no honest `?q=` for the cost report today, so the row says the
    // figure is this project's and the screen behind it is the platform's,
    // rather than implying a narrowed report.
    expect(handoff("cost")?.textContent).toContain(
      "the report behind it is the platform's"
    )
  })

  it("does not render a hand-off this session cannot reach", async () => {
    // A viewer on the project and nothing on the platform: `runs.view` is the
    // whole of what a viewer holds.
    mount(["/projects/p_atlas"], {
      roles: [],
      projectRoles: { p_atlas: ["viewer"] },
    })

    await screen.findByRole("heading", { name: "Atlas" })

    expect(handoff("runs")).not.toBeNull()
    // Hidden, not refused: navigation a role cannot use is not shown at all.
    // Only an *act* stays visible and explains itself.
    expect(handoff("sources")).toBeNull()
    expect(handoff("cost")).toBeNull()
    expect(handoff("queue")).toBeNull()
  })

  it("asks each permission against this project rather than about the shift", async () => {
    // The same person, a project-admin next door and nothing here. The page is
    // atlas's, so atlas is what answers.
    mount(["/projects/p_atlas"], {
      roles: [],
      projectRoles: { p_plexor: ["project-admin"], p_atlas: ["viewer"] },
    })

    await screen.findByRole("heading", { name: "Atlas" })
    expect(handoff("sources")).toBeNull()
  })
})

describe("who holds which role on this project", () => {
  it("lists the assignments, and only this project's", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })
    const grants = await waitFor(() => {
      const found = at("project-grants")
      expect(found).not.toBeNull()
      return found
    })

    expect(grants?.textContent).toContain("duty@comuki.local")
    expect(grants?.textContent).toContain("nadia@plexor.dev")
    expect(grants?.textContent).toContain("ines@atlas.example")
    expect(grants?.textContent).toContain("project-admin")
    expect(grants?.textContent).toContain("2026-06-27")
    // A platform grant holds everywhere and is not an assignment *on* this
    // project — listing it here would say somebody was given something they
    // were not.
    expect(grants?.textContent).not.toContain("rhea@comuki.local")
  })

  it("lands the way out on the grants this project's slug names", async () => {
    mount(["/projects/p_atlas"])

    await screen.findByRole("heading", { name: "Atlas" })
    await waitFor(() => expect(at("project-grants")).not.toBeNull())

    // The grants list matches on its scope label, and a project's scope label
    // *is* its slug — so the link lands narrowed rather than on every grant on
    // the platform.
    expect(at("project-grants-all")?.getAttribute("href")).toBe(
      "/identity?tab=grants&q=atlas"
    )
    expect(at("project-grant-new")?.getAttribute("href")).toBe(
      "/identity/grants/new"
    )
  })

  it("says which role would open it rather than hiding the region", async () => {
    // Platform ops: everything on the lower rail, and not identity.
    mount(["/projects/p_atlas"], { roles: ["operator"] })

    await screen.findByRole("heading", { name: "Atlas" })

    // Rendered, not silently dropped: an administrator reading somebody else's
    // screen has to learn that the region exists and what it needs.
    const forbidden = await waitFor(() => {
      const found = at("forbidden-state")
      expect(found).not.toBeNull()
      return found
    })
    expect(forbidden?.textContent).toContain("Roles on this project")
    expect(forbidden?.textContent).toContain("platform-admin")
    expect(at("project-grants")).toBeNull()
  })

  it("says in words when nobody holds a role on it", async () => {
    mount(["/projects/p_vega"])

    await screen.findByRole("heading", { name: "Vega" })

    const empty = await waitFor(() => {
      const found = at("project-no-grants")
      expect(found).not.toBeNull()
      return found
    })
    // A real answer, and a common one on a project's first day — not an empty
    // box, which reads as a broken render.
    expect(empty?.textContent).toContain("platform grant")
  })
})

describe("a project with nothing measured yet", () => {
  it("renders an unmeasured spend as a dash, never as zero", async () => {
    mount(["/projects/p_vega"])

    await screen.findByRole("heading", { name: "Vega" })

    // `null` is not `0`: zero is a project that ran and cost nothing, `null` is
    // a project the cost report has never heard of. `$0.00` would tell the
    // operator a two-day-old project is already accounted for.
    expect(handoff("cost")?.textContent).toContain("—")
    expect(handoff("cost")?.textContent).not.toContain("$0.00")
    expect(handoff("cost")?.textContent).toContain("nothing attributed yet")
  })

  it("counts a project with no runs, no queue and no connections as zero", async () => {
    mount(["/projects/p_vega"])

    await screen.findByRole("heading", { name: "Vega" })

    expect(handoff("runs")?.textContent).toContain("0 in flight of 0 seen")
    await waitFor(() =>
      expect(handoff("queue")?.textContent).toContain(
        "0 work items · 0 workers up"
      )
    )
    await waitFor(() =>
      expect(handoff("sources")?.textContent).toContain("0 connections")
    )
  })

  it("says the repository is the platform's defaults rather than missing", async () => {
    mount(["/projects/p_vega"])

    await screen.findByRole("heading", { name: "Vega" })
    expect(at("project-facts")?.textContent).toContain("platform defaults")
  })
})

describe("an address that outlived the project it named", () => {
  it("names the id it could not find", async () => {
    const router = mount(["/projects/p_gone"])

    const state = await waitFor(() => {
      const found = at("project-not-found")
      expect(found).not.toBeNull()
      return found
    })

    // The id is the only part of a dead link the operator can take back to
    // whoever wrote it, so the state says it out loud.
    expect(state?.textContent).toContain("p_gone")
    expect(state?.textContent).toContain("old link")
    expect(here(router)).toBe("/projects/p_gone")
  })

  it("puts the registry one press away", async () => {
    const router = mount(["/projects/p_gone"])

    await waitFor(() => expect(at("project-not-found")).not.toBeNull())
    fireEvent.click(at("project-not-found-back") as Element)

    await waitFor(() => expect(here(router)).toBe("/projects"))
  })

  it("draws no facts and no hand-offs for a project that is not there", async () => {
    mount(["/projects/p_gone"])

    await waitFor(() => expect(at("project-not-found")).not.toBeNull())

    expect(at("project-facts")).toBeNull()
    expect(at("project-handoffs")).toBeNull()
    expect(handoff("runs")).toBeNull()
  })
})
