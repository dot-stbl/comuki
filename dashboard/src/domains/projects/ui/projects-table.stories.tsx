import { createContext, useContext, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { buildProjectRows } from "@/domains/projects/model/activity"
import {
  createProjectColumns,
  getProjectId,
} from "@/domains/projects/ui/projects-columns"
import { COST_SEED } from "@/shared/api/mock/cost.seed"
import { PLATFORM_PROJECTS_SEED } from "@/shared/api/mock/projects.seed"
import { RUNS_SEED } from "@/shared/api/mock/runs.seed"
import { DataTable } from "@/shared/ui"

const columns = createProjectColumns()
const rows = buildProjectRows(PLATFORM_PROJECTS_SEED, RUNS_SEED, COST_SEED.byApp)

/* The slug cell is a real anchor into the project's own screen, so the table
   only exists inside a router now. A memory router carrying the one
   destination gives the story a working link without dragging in the route
   tree. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })

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

function Frame({ children }: { children: ReactNode }) {
  return (
    <SlotContext value={children}>
      {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
      <RouterProvider router={router as any} />
    </SlotContext>
  )
}

const meta: Meta<typeof DataTable> = {
  title: "Projects/Registry",
  component: DataTable,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof DataTable>

/**
 * The registry as seeded. Three projects with work in them and one created two
 * days ago — the last row is the point: no runs, no spend, no profile
 * repository, and every derived column degrades to a dash or to the fact that
 * it is running on the platform defaults.
 */
export const Seeded: Story = {
  render: () => (
    <Frame>
      <DataTable
        columns={columns}
        data={rows}
        getRowId={getProjectId}
        density="compact"
      />
    </Frame>
  ),
}

/** Nothing has been created yet — the first thing a platform ever looks like. */
export const Empty: Story = {
  render: () => (
    <Frame>
      <DataTable
        columns={columns}
        data={[]}
        getRowId={getProjectId}
        density="compact"
        emptyLabel="no projects yet"
      />
    </Frame>
  ),
}
