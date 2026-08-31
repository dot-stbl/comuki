import { createContext, useContext, useState } from "react"
import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { Plus } from "lucide-react"

import { Button, DataTableToolbar, type DataColumn } from "@/shared/ui"

import { PageHeader } from "./page-header"
import { RailContext } from "./rail-context"

/** Enough of a column set for the toolbar to assemble a filter bar from. */
interface StoryRun {
  status: string
  app: string
  task: string
  profile: string
}

const FILTER_COLUMNS: DataColumn<StoryRun>[] = [
  {
    accessorKey: "status",
    header: "status",
    meta: {
      filter: {
        kind: "select",
        placeholder: "all statuses",
        options: [
          { value: "running", label: "running" },
          { value: "waiting", label: "waiting" },
        ],
      },
    },
  },
  {
    accessorKey: "app",
    header: "app",
    meta: {
      filter: {
        kind: "select",
        placeholder: "all apps",
        options: [
          { value: "plexor", label: "plexor" },
          { value: "storefront", label: "storefront" },
        ],
      },
    },
  },
  {
    accessorKey: "task",
    header: "task",
    meta: {
      filter: { kind: "text", placeholder: "search run, task, step…" },
    },
  },
  {
    accessorKey: "profile",
    header: "profile",
    meta: {
      filter: {
        kind: "select",
        placeholder: "all profiles",
        options: [
          { value: "planner", label: "planner" },
          { value: "builder", label: "builder" },
        ],
      },
    },
  },
]

/* The crumbs are real `<Link>`s, so the header only renders inside a router.
   A memory router with the product's own paths gives the story working links
   without dragging in the app's route tree. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/tasks", "/settings", "/cost", "/knowledge"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

/**
 * The rail lives in `AppShell`, which the story does not mount, so the toggle
 * gets a local state to flip: the control has to be able to show both faces.
 */
function Frame({ children }: { children: ReactNode }) {
  const [railCollapsed, setRailCollapsed] = useState(false)

  return (
    <RailContext
      value={{
        railCollapsed,
        toggleRail: () => setRailCollapsed((open) => !open),
      }}
    >
      <SlotContext value={children}>
        <RouterProvider router={router} />
      </SlotContext>
    </RailContext>
  )
}

const meta: Meta<typeof PageHeader> = {
  title: "Shell/PageHeader",
  component: PageHeader,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  decorators: [(Story) => <Frame>{<Story />}</Frame>],
}

export default meta
type Story = StoryObj<typeof PageHeader>

/**
 * The whole contract in one screen: rail control, crumb path, title, summary.
 * The connector between crumbs is the profile river's own link bar, so a path
 * reads as a flow rather than as a file system.
 */
export const Default: Story = {
  args: {
    breadcrumbs: [{ label: "observe", to: "/runs" }, { label: "cost" }],
    title: "Cost & failures",
    summary: "last 24h",
  },
}

/** A section's own landing screen has no ancestor, so it has one crumb. */
export const SingleCrumb: Story = {
  args: {
    breadcrumbs: [{ label: "settings" }],
    title: "Settings",
    summary: "control plane configuration",
  },
}

/** Screen controls sit at the right of the crumb line, opposite the rail. */
export const WithActions: Story = {
  args: {
    breadcrumbs: [{ label: "tasks" }],
    title: "Tasks",
    summary: "18 in backlog · 4 new",
    actions: (
      <Button size="sm">
        <Plus aria-hidden="true" />
        New task
      </Button>
    ),
  },
}

/**
 * The filter bar in the band that never scrolls. `AppShell` pins the header
 * above the screen's scroll port, so the controls that decide which rows a
 * screen is showing stay put while the rows move — which is the whole reason
 * a table screen hands its `DataTableToolbar` up here rather than stacking it
 * on top of the table. `actions` still carries the screen's verbs; the two
 * slots are split by what a control acts on, not by how it looks.
 */
export const WithFilters: Story = {
  args: {
    breadcrumbs: [{ label: "live runs" }],
    title: "Live runs",
    summary: "24 runs · 9 running · 3 waiting on a human",
    actions: (
      <Button variant="ghost" size="sm">
        Collapse flow
      </Button>
    ),
    filters: (
      <DataTableToolbar
        columns={FILTER_COLUMNS}
        filters={{}}
        onFiltersChange={() => {}}
        trailing={<span>24 shown</span>}
      />
    ),
  },
}

/**
 * The same band with three filters on. The chips are the answer to "what is
 * this list narrowed to" without opening anything; the search field and the
 * button have not moved a pixel from the story above.
 */
export const WithActiveFilters: Story = {
  args: {
    breadcrumbs: [{ label: "live runs" }],
    title: "Live runs",
    summary: "24 runs · 9 running · 3 waiting on a human",
    filters: (
      <DataTableToolbar
        columns={FILTER_COLUMNS}
        filters={{ status: "waiting", app: "plexor", profile: "planner" }}
        onFiltersChange={() => {}}
        trailing={<span>3 shown</span>}
      />
    ),
  },
}

/** A deeper path, where the connector is doing the work it exists for. */
export const DeepPath: Story = {
  args: {
    breadcrumbs: [
      { label: "observe", to: "/runs" },
      { label: "live runs", to: "/runs" },
      { label: "run_8f21c4" },
    ],
    title: "Split checkout totals into a pricing service",
    summary: "storefront",
  },
}

/** No summary, no actions — the least a header can be. */
export const TitleOnly: Story = {
  args: {
    breadcrumbs: [{ label: "knowledge" }],
    title: "Knowledge",
  },
}
