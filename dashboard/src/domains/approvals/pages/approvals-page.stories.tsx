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

import { approvalsQueryKey } from "@/domains/approvals/api/queries"
import { SessionProvider } from "@/shared/session"

import { ApprovalsPage } from "./approvals-page"

/* The approvals queue — a stack of cards the duty engineer works through.
   Each card is a decision the swarm is waiting on, and the queue's whole
   job is to make the next decision the cheapest act on the page.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the queue holds the seed store, so the queue renders
   however many cards the seed says it holds. The mutation rides the same
   store — confirming a card here settles the queue exactly as it would in
   the app. */

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
  history: createMemoryHistory({ initialEntries: ["/approvals"] }),
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

const meta: Meta<typeof ApprovalsPage> = {
  title: "Pages/Approvals",
  component: ApprovalsPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ApprovalsPage>

/**
 * The full queue: every card carries its subject, its project, and the
 * decision that is owed. The operator works top to bottom; the band on a
 * card is the act itself, not a navigation to one.
 */
export const Default: Story = {
  render: () => (
    <Frame>
      <ApprovalsPage />
    </Frame>
  ),
}

/**
 * Nothing waiting on a person, pinned honestly: the approvals cache is
 * seeded empty before the page mounts. The page says so in its own words
 * rather than drawing a blank region — a decided queue is a quiet queue,
 * and the operator should read that as the day's state, not as a fault.
 */
export const NothingWaiting: Story = {
  render: () => (
    <Frame
      seed={(client) => {
        client.setQueryData(approvalsQueryKey, [])
      }}
    >
      <ApprovalsPage />
    </Frame>
  ),
}
