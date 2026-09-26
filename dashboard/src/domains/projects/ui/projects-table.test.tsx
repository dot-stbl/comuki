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
    // The full identity: a stored icon over a GitHub URL — the override has
    // to win over the derivation — plus a colour and a vocabulary.
    icon: "🛰️",
    color: "#3c5a86",
    tags: ["platform", "orchestration"],
  },
  {
    // GitLab-hosted and bare: the mark derives from the host, which is a
    // real rendering path rather than a corner case.
    id: "p_plexor",
    slug: "plexor",
    name: "Plexor",
    gitProfileRepo: "git@gitlab.com:plexor/agent-profiles.git",
    createdAt: "2026-05-19",
    tags: ["agent"],
  },
  {
    // Created two days ago: no runs, no spend, no repository, no identity.
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
    // Vega's four: nothing in flight, no runs, nothing spent, no tags — each
    // absent the same way the derived columns were always absent.
    expect(await screen.findByText("vega")).toBeTruthy()
    const vegaDashes = [
      ...findRow("vega").querySelectorAll('[class*="absent"]'),
    ].filter((node) => node.textContent === "—")
    expect(vegaDashes.length).toBe(4)
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

describe("the registry's identity column", () => {
  it("shows a stored icon, and the stored icon wins over the derivation", async () => {
    mount()

    expect(await screen.findByText("🛰️")).toBeTruthy()
    // comuki's repository is GitHub-hosted; the override means the row must
    // not draw the Octocat. The only brand mark on screen is plexor's tanuki.
    await screen.findByText("plexor")
    const brands = [...document.querySelectorAll('[data-test="brand-icon"]')]
    expect(brands.map((node) => node.getAttribute("data-brand"))).toEqual([
      "gitlab",
    ])
  })

  it("derives the GitLab mark from the repository host", async () => {
    mount()

    await screen.findByText("plexor")
    const brands = [
      ...document.querySelectorAll('[data-test="brand-icon"]'),
    ].map((node) => node.getAttribute("data-brand"))

    // plexor's repository is GitLab-hosted and it stores no icon, so the
    // tanuki is derived. A GitHub-derived row appears in the page-level
    // tests through the seed's GitHub-hosted projects.
    expect(brands).toContain("gitlab")
  })

  it("renders the tags as chips and the bare row without them", async () => {
    mount()

    expect(await screen.findByText("🛰️")).toBeTruthy()
    expect(screen.getByText("platform")).toBeTruthy()
    expect(screen.getByText("orchestration")).toBeTruthy()
    expect(screen.getByText("agent")).toBeTruthy()
    // Vega has no vocabulary: the tags cell says so with the same dash every
    // other absent fact gets, not with a blank — counted in the case above.
    expect(
      findRow("vega").querySelectorAll('[data-test="project-tag"]').length
    ).toBe(0)
  })

  it("paints the accent dot from the row's stored colour", async () => {
    mount()

    await screen.findByText("comuki")
    const identity = document.querySelector(
      '[data-test="project-link"]'
    )?.parentElement
    // The one custom property (design D7): comuki's row carries it, and the
    // stylesheet — not the markup — turns it into the dot. Vega's row, with
    // no colour, carries no style at all — the dot falls back in CSS.
    expect(identity?.getAttribute("style")).toContain("--project-accent")
    expect(identity?.getAttribute("style")).toContain("#3c5a86")
    const vegaRow = findRow("vega")
    expect(
      vegaRow
        .querySelector('[data-test="project-link"]')
        ?.parentElement?.getAttribute("style")
    ).toBeNull()
  })
})

/* The virtualized body renders rows in a portal measured against a fixed
   port; this finds the row's <tr> once its slug link has appeared. */
function findRow(slug: string): HTMLElement {
  const link = [
    ...document.querySelectorAll('[data-test="project-link"]'),
  ].find((node) => node.textContent === slug)
  const row = link?.closest("tr")
  if (!row) {
    throw new Error(`row ${slug} did not render`)
  }
  return row as HTMLElement
}
