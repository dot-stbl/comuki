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

import { tasksQueryKey } from "@/domains/tasks/api/queries"
import { SessionProvider } from "@/shared/session"

import { TasksPage } from "./tasks-page"

/* The tasks / inbox — the pending backlog the operator works through to
   launch work into the swarm. Each row is one ticket, the dispatch on a
   row is the act that hands it to the orchestrator.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the inbox reads the mutable seed store. A story that says
   "four tickets waiting" actually has four rows the operator can dispatch.
   The mutation rides the same store — dispatching here settles the row
   exactly as it would in the app. The two stories below differ in the
   `focus` the address bar carried. */

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
    "/tasks/new",
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
  history: createMemoryHistory({ initialEntries: ["/tasks"] }),
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

const meta: Meta<typeof TasksPage> = {
  title: "Pages/Tasks",
  component: TasksPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof TasksPage>

/**
 * The full inbox — every ticket the swarm has not yet picked up, in the
 * order the queue hands them out. The dispatch is the act on the row, and
 * one click is one ticket handed to the orchestrator.
 */
export const InboxOpen: Story = {
  render: () => (
    <Frame>
      <TasksPage />
    </Frame>
  ),
}

/**
 * Arrived at the inbox with a focus — the address bar said `?q=…`, so the
 * toolbar opens narrowed. The chip says where the narrowing came from, and
 * clearing it returns to the full list.
 */
export const InboxNarrowed: Story = {
  render: () => (
    <Frame>
      <TasksPage focus="checkout" />
    </Frame>
  ),
}

/**
 * The inbox is empty, pinned honestly: the tasks cache is seeded with an
 * empty list before the page mounts, so the page says "no tickets waiting"
 * in its own words — a state worth saying out loud, because a screen that
 * drew nothing else would leave the operator wondering whether it had
 * loaded at all.
 */
export const InboxEmpty: Story = {
  render: () => (
    <Frame
      seed={(client) => {
        client.setQueryData(tasksQueryKey, [])
      }}
    >
      <TasksPage />
    </Frame>
  ),
}
