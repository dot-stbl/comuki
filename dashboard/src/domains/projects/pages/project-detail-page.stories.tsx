import { createContext, useContext, useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { ProjectDetailPage } from "@/domains/projects/pages/project-detail-page"
import { SessionProvider } from "@/shared/session"
import type { Role } from "@/shared/session"

/* The crumbs are real `<Link>`s, every hand-off is a real anchor and the shell
   renders the whole rail, so this screen only exists inside a router, a
   session and a query client — the same three the app hands it. A memory
   router carrying the product's own paths gives the story working links
   without dragging in the generated route tree. `form-page.stories.tsx` is the
   harness this follows. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const routeTree = rootRoute.addChildren(
  [
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
    "/projects",
    "/compute",
    "/models",
    "/observability",
    "/components",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

/** Every project the seeded registry holds — what the session can see. */
const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_vega", key: "vega", name: "Vega" },
]

function Frame({
  roles,
  children,
}: {
  roles: Role[]
  children: ReactNode
}) {
  /* Held rather than rebuilt: this screen runs four queries, and a client
     constructed inside the render would hand each of them a new cache on every
     pass. */
  const [client] = useState(
    () => new QueryClient({ defaultOptions: { queries: { retry: false } } })
  )

  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Rhea Okafor",
        email: "rhea@comuki.local",
        platformRoles: roles,
        projectRoles: {},
      }}
      projects={PROJECTS}
    >
      <QueryClientProvider client={client}>
        <SlotContext value={children}>
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </SlotContext>
      </QueryClientProvider>
    </SessionProvider>
  )
}

/**
 * One project, in full: its record, who holds a role on it, and a count and a
 * link for every screen its work actually lives on.
 *
 * The page draws no runs table, no queue table and no cost breakdown, and that
 * is the whole design — a second duty screen is a screen that can disagree
 * with the first one, and the day it does the operator believes whichever they
 * are standing on.
 */
const meta: Meta<typeof ProjectDetailPage> = {
  title: "Projects/ProjectDetail",
  component: ProjectDetailPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ProjectDetailPage>

/**
 * The live reading. `comuki` has a profile repository, runs in flight, a line
 * in the cost report, work in the queue, containers up and two people holding
 * a role on it — every fact on the page is present and every hand-off carries
 * a figure.
 */
export const LiveReading: Story = {
  render: () => (
    <Frame roles={["platform-admin"]}>
      <ProjectDetailPage projectId="p_comuki" />
    </Frame>
  ),
}

/**
 * The empty reading, and the one worth looking at twice. `vega` is two days
 * old: no runs, no spend, no repository, nothing in the queue, no connections
 * and nobody holding a role on it.
 *
 * Every derived fact on the page degrades, and each one degrades to a
 * *different* answer — the repository says `platform defaults` because that is
 * a legitimate configuration, the spend says a dash because it has not been
 * measured rather than because it is zero, and the roles region says in words
 * that a platform grant is how anyone reaches this project at all.
 */
export const EmptyReading: Story = {
  render: () => (
    <Frame roles={["platform-admin"]}>
      <ProjectDetailPage projectId="p_vega" />
    </Frame>
  ),
}

/**
 * The same project read by platform ops. Identity is a platform-admin act, so
 * the roles region says which role would open it rather than disappearing —
 * an administrator reading somebody else's screen has to learn the region is
 * there. Sources is a project act this role does not hold, so that hand-off is
 * *hidden*: navigation a role cannot use is not shown at all.
 */
export const RolesClosed: Story = {
  render: () => (
    <Frame roles={["operator"]}>
      <ProjectDetailPage projectId="p_comuki" />
    </Frame>
  ),
}

/**
 * An address that outlived the project it named. The state says the id out
 * loud, because it is the only part of a dead link the operator can take back
 * to whoever wrote it.
 */
export const Missing: Story = {
  render: () => (
    <Frame roles={["platform-admin"]}>
      <ProjectDetailPage projectId="p_gone" />
    </Frame>
  ),
}
