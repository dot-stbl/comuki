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

import {
  queueQueryKey,
  workersQueryKey,
} from "@/domains/queue/api/queries"
import { SessionProvider } from "@/shared/session"

import { QueuePage } from "./queue-page"

/* Queue & workers — the screen that joins the two halves a stuck swarm
   produces. A worker holding a lease it stopped defending is a worker the
   queue never noticed; an item nobody claims is a fault only the pool can
   explain.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the queue half reads the seed store, the worker half reads
   its own. The page renders whatever rows the seeds happen to hold today. */

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
  history: createMemoryHistory({ initialEntries: ["/queue"] }),
})

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
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

const meta: Meta<typeof QueuePage> = {
  title: "Pages/Queue",
  component: QueuePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof QueuePage>

/**
 * The full board: items queued, depth by day, and the pool above. The
 * two halves share a screen because neither answers its own question —
 * "queued eleven minutes" is only a fault once you can see there was an
 * idle worker to take it.
 */
export const Default: Story = {
  render: () => (
    <Frame>
      <QueuePage />
    </Frame>
  ),
}

/**
 * A dormant board, pinned honestly: both caches — the queue items and the
 * worker registry — are seeded empty before the page mounts, so each half
 * draws its own empty reading. Nothing queued and nobody up is a real
 * state for an ephemeral swarm between shifts, and the page says it rather
 * than leaving two blank panels.
 */
export const DormantBoard: Story = {
  render: () => (
    <Frame
      seed={(client) => {
        client.setQueryData(queueQueryKey, {
          items: [],
          pools: [],
          depth: [],
        })
        client.setQueryData(workersQueryKey, [])
      }}
    >
      <QueuePage />
    </Frame>
  ),
}
