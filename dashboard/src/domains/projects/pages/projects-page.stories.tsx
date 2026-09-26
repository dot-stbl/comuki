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

import { SessionProvider } from "@/shared/session"

import { ProjectsPage } from "./projects-page"

/* The platform registry — every project, what it is running, what it costs.
   The lower tier of the rail, visited on a different clock from the duty
   screens and usually to create something or to answer what a project is
   spending.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the registry reads the mutable seed store, joined with what
   each project is doing today. The two stories below differ in the `focus`
   the address bar carried — one arrived unfiltered, the other opened
   narrowed to a single project. */

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
    "/projects",
    "/projects/new",
    "/projects/$projectId",
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
  history: createMemoryHistory({ initialEntries: ["/projects"] }),
})

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
]

function Frame({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: { queries: { retry: false, staleTime: Infinity } },
      })
  )

  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Rhea Okafor",
        email: "rhea@comuki.local",
        platformRoles: ["platform-admin"],
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

const meta: Meta<typeof ProjectsPage> = {
  title: "Pages/Projects",
  component: ProjectsPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ProjectsPage>

/**
 * The platform registry in full: every project, what it is running, what
 * it is spending. The summary figures in the header — the count, the
 * in-flight runs, the total spend today — are derived from the rows below
 * them, so the header and the list cannot disagree about what the platform
 * is doing.
 */
export const Registry: Story = {
  render: () => (
    <Frame>
      <ProjectsPage />
    </Frame>
  ),
}

/**
 * Arrived at the page with a focus — the address bar said `?q=comuki`, so
 * the toolbar opens narrowed to the platform itself. The row says where
 * the list came from without saying so out loud: the operator can clear
 * the chip and see the rest, or leave it and trust that the page knows
 * why they were sent here.
 */
export const ArrivedNarrowed: Story = {
  render: () => (
    <Frame>
      <ProjectsPage focus="comuki" />
    </Frame>
  ),
}
