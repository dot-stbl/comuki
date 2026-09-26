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

import { runsQueryKey } from "@/domains/runs/api/queries"
import { SessionProvider } from "@/shared/session"

import { RunsPage } from "./runs-page"

/* The duty list — live runs, the screen visited most. The header's
   profile river is the first thing the operator reads; the table underneath
   is the second.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the runs read the mutable seed store, so the page renders
   whatever rows the seed happens to hold. The mutations ride the same
   store — cancelling here settles the row exactly as it would in the app. */

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
    "/runs/$runId",
    "/queue",
    "/approvals",
    "/cost",
    "/sources",
    "/knowledge",
    "/verify",
    "/settings",
    "/identity",
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
  history: createMemoryHistory({ initialEntries: ["/runs"] }),
})

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
]

function Frame({
  seed,
  children,
}: {
  seed?: (client: QueryClient) => void
  children: ReactNode
}) {
  const [client] = useState(() => {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity } },
    })
    seed?.(client)
    return client
  })

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

const meta: Meta<typeof RunsPage> = {
  title: "Pages/Runs",
  component: RunsPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RunsPage>

/**
 * The full duty list: the river at the top names what the swarm is
 * working on, the table underneath is the rows the river came from. A
 * profile node the operator presses narrows the table to that profile —
 * the river and the table are two views of the same list, and the page
 * keeps them in step.
 */
export const Default: Story = {
  render: () => (
    <Frame>
      <RunsPage />
    </Frame>
  ),
}

/**
 * An empty swarm, pinned honestly: the runs cache is seeded with an empty
 * list before the page mounts, so the river has no columns to draw and the
 * table renders its own "no runs in flight" reading — a sentence the page
 * says on purpose, not a blank screen that leaves the operator guessing
 * whether it loaded.
 */
export const EmptySwarm: Story = {
  render: () => (
    <Frame
      seed={(client) => {
        client.setQueryData(runsQueryKey, [])
      }}
    >
      <RunsPage />
    </Frame>
  ),
}
