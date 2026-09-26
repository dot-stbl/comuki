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

import { HomePage } from "./home-page"

/* The home screen is the operator's first stop — the page that answers
   "am I needed right now, and where". The crumbs are real `<Link>`s and the
   shell draws the whole rail, so the screen only renders inside a router, a
   session and a query client — the same three the app hands it. A memory
   router carrying the product's own paths gives the story working crumbs
   without dragging in the generated route tree; `form-page.stories.tsx` is the
   harness this follows.

   Mock-first: the query reads the seed store, and a story that needs a
   different shift pins the cache rather than rewriting the store — the
   client is built with `staleTime: Infinity` and seeded once, so the query
   never refetches and the page truthfully reads what the story installed. */

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
    "/runs/$runId",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
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

const meta: Meta<typeof HomePage> = {
  title: "Pages/Home",
  component: HomePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof HomePage>

/**
 * The home screen as it reads on a shift — the verdict names the figure,
 * the list shows the runs behind it, and the band below says what the
 * swarm did last week. The page renders one column and scrolls: the
 * verdict, then the runs that owe a decision, then the running tail, then
 * shortcuts. A quiet shift and a busy one look different only because the
 * seed is different — the page does not branch on the count.
 */
export const Default: Story = {
  render: () => (
    <Frame>
      <HomePage />
    </Frame>
  ),
}

/**
 * A quiet shift, honestly held: the runs cache is pinned empty before the
 * page mounts, so the verdict reads "nobody needs me" because the data says
 * so — not because the store happened to be reset. The outcomes band still
 * carries the week's history, which is true of a quiet shift too: the swarm
 * worked, it just has nothing waiting on a person right now.
 */
export const QuietShift: Story = {
  render: () => (
    <Frame
      seed={(client) => {
        client.setQueryData(runsQueryKey, [])
      }}
    >
      <HomePage />
    </Frame>
  ),
}
