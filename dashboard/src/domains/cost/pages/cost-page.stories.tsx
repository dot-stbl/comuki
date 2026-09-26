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

import { CostPage } from "./cost-page"

/* Cost & failures — the slow-clock reading of what the swarm is doing with
   the budget. Three tiles across the top (today / forecast / budget) and
   the breakdowns underneath, in the order the operator reads them.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the figures come from the cost seed, so the page renders
   today's snapshot. The `cost-mock-mark` strip at the bottom is the only
   thing the page draws that distinguishes mock from real, and it stays. */

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
  history: createMemoryHistory({ initialEntries: ["/cost"] }),
})

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
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

const meta: Meta<typeof CostPage> = {
  title: "Pages/Cost",
  component: CostPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof CostPage>

/**
 * Today, in full. The three tiles across the top show where the day is
 * going; the breakdowns underneath name each shape the operator reads in a
 * glance — spend by day, by model, top projects, by app, and where runs
 * fail. The delta chip rides beside the period toggle: a number that says
 * whether today is hotter than the previous one, with the arrow a colour
 * and not a word.
 */
export const Default: Story = {
  render: () => (
    <Frame>
      <CostPage />
    </Frame>
  ),
}
