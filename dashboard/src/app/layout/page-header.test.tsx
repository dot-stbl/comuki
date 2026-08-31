import { createContext, useContext } from "react"
import type { ReactNode } from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { DataTableToolbar, type DataColumn } from "@/shared/ui"

import { PageHeader } from "./page-header"
import { RailContext, type RailState } from "./rail-context"

/* The crumbs are real links, so the header only renders inside a router. A
   memory router carrying the product's own paths keeps the test off the app's
   generated route tree. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/settings"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

function renderHeader(node: ReactNode, rail?: Partial<RailState>) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  })

  return render(
    <RailContext value={{ railCollapsed: false, toggleRail: () => {}, ...rail }}>
      <SlotContext value={node}>
        <RouterProvider router={router} />
      </SlotContext>
    </RailContext>
  )
}

/* The product marks its own landmarks with `data-test`, not `data-testid`, so
   the bands are found the way every other screen finds them. */
const header = (container: HTMLElement) =>
  container.querySelector<HTMLElement>('[data-test="page-header"]')

const filterBand = (container: HTMLElement) =>
  container.querySelector<HTMLElement>('[data-test="page-header-filters"]')

describe("PageHeader", () => {
  it("links every crumb but the last, and marks the last as the page", async () => {
    renderHeader(
      <PageHeader
        breadcrumbs={[{ label: "observe", to: "/runs" }, { label: "cost" }]}
        title="Cost & failures"
      />
    )

    // The path is a landmark, so a screen reader can jump to it by name.
    await screen.findByRole("navigation", { name: "Breadcrumb" })

    const ancestor = screen.getByRole("link", { name: "observe" })
    expect(ancestor.getAttribute("href")).toBe("/runs")

    // The page you are already on is not a link, and says so.
    expect(screen.queryByRole("link", { name: "cost" })).toBeNull()
    expect(screen.getByText("cost").getAttribute("aria-current")).toBe("page")
  })

  it("names the screen once, as the heading", async () => {
    renderHeader(
      <PageHeader breadcrumbs={[{ label: "settings" }]} title="Settings" />
    )

    const heading = await screen.findByRole("heading", { level: 1 })
    expect(heading.textContent).toBe("Settings")
  })

  it("exposes the rail control's target and state, and toggles it", async () => {
    const toggleRail = vi.fn()
    renderHeader(
      <PageHeader breadcrumbs={[{ label: "settings" }]} title="Settings" />,
      { railCollapsed: true, toggleRail }
    )

    const toggle = await screen.findByRole("button", {
      name: "Show the navigation rail",
    })
    expect(toggle.getAttribute("aria-controls")).toBe("rail")
    expect(toggle.getAttribute("aria-expanded")).toBe("false")

    await userEvent.click(toggle)
    expect(toggleRail).toHaveBeenCalledTimes(1)
  })

  it("carries a screen's filter bar in the band that never scrolls", async () => {
    // The point of the slot: `AppShell` pins this header above the scroll port,
    // so a filter handed here cannot scroll away from the list it narrows.
    const { container } = renderHeader(
      <PageHeader
        breadcrumbs={[{ label: "live runs" }]}
        title="Live runs"
        summary="24 runs"
        filters={<input aria-label="Filter by task" />}
      />
    )

    await screen.findByRole("heading", { level: 1 })
    const band = filterBand(container)
    const control = screen.getByRole("textbox", { name: "Filter by task" })

    expect(band?.contains(control)).toBe(true)
    expect(header(container)?.contains(band)).toBe(true)
  })

  it("keeps the filter band out of a header that has no filters", async () => {
    const { container } = renderHeader(
      <PageHeader breadcrumbs={[{ label: "settings" }]} title="Settings" />
    )

    await screen.findByRole("heading", { level: 1 })
    // An empty band is still a band: it would add the header's own gap under
    // every screen that has nothing to put in it.
    expect(filterBand(container)).toBeNull()
  })

  it("carries the whole filter row — search, button and chips — in that band", async () => {
    // The slot takes a `DataTableToolbar` whole. What lands in the pinned band
    // is the search field, the button holding the rest, and one chip per
    // filter that is on — all three inside the header, none of them below it.
    interface Row {
      status: string
      task: string
    }
    const columns: DataColumn<Row>[] = [
      {
        accessorKey: "status",
        header: "status",
        meta: {
          filter: {
            kind: "select",
            placeholder: "all statuses",
            options: [{ value: "waiting", label: "waiting on a human" }],
          },
        },
      },
      { accessorKey: "task", header: "task", meta: { filter: { kind: "text" } } },
    ]

    const { container } = renderHeader(
      <PageHeader
        breadcrumbs={[{ label: "live runs" }]}
        title="Live runs"
        filters={
          <DataTableToolbar
            columns={columns}
            filters={{ status: "waiting" }}
            onFiltersChange={() => {}}
          />
        }
      />
    )

    await screen.findByRole("heading", { level: 1 })
    const band = filterBand(container)

    expect(band?.contains(screen.getByRole("searchbox"))).toBe(true)
    expect(
      band?.contains(screen.getByRole("button", { name: "Filters, 1 active" }))
    ).toBe(true)
    expect(
      band?.contains(
        screen.getByRole("button", {
          name: "Clear the status filter: waiting on a human",
        })
      )
    ).toBe(true)
  })

  it("keeps actions and filters apart", async () => {
    // The split is what a control acts on: `actions` are verbs aimed at the
    // screen, `filters` decide which rows the screen is showing. A screen that
    // passes both must get both, in their own bands.
    const { container } = renderHeader(
      <PageHeader
        breadcrumbs={[{ label: "tasks" }]}
        title="Tasks"
        actions={<button type="button">New task</button>}
        filters={<input aria-label="Filter by title" />}
      />
    )

    await screen.findByRole("heading", { level: 1 })
    const band = filterBand(container)

    expect(
      band?.contains(screen.getByRole("button", { name: "New task" }))
    ).toBe(false)
    expect(
      band?.contains(screen.getByRole("textbox", { name: "Filter by title" }))
    ).toBe(true)
  })
})
