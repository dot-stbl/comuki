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

import { sourcesQueryKey } from "@/domains/sources/api/queries"
import { SessionProvider } from "@/shared/session"

import { SourcesPage } from "./sources-page"

/* Where work comes from. Every connection on the platform — trackers,
   ticket systems, webhooks — and the decisions that are decisions about a
   row, not edits of one.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the registry reads the mutable seed store. A story that
   says "three connections, one native" actually has the rows the operator
   would see if they had just signed in. The two stories below differ in
   the `focus` the address bar carried. */

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
    "/sources/new",
    "/sources/$sourceId",
    "/sources/$sourceId/ticket/new",
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
  history: createMemoryHistory({ initialEntries: ["/sources"] }),
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

const meta: Meta<typeof SourcesPage> = {
  title: "Pages/Sources",
  component: SourcesPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof SourcesPage>

/**
 * The registry in full: every connection on the platform, with the
 * project it belongs to, the host it speaks to and the state it is in. The
 * decisions a row owns (test, disconnect) ride on the row because answering
 * either on a page the operator had to travel to would lose their place in
 * the list they are working down.
 */
export const Registry: Story = {
  render: () => (
    <Frame>
      <SourcesPage />
    </Frame>
  ),
}

/**
 * Arrived at the page with a focus — the address bar said `?q=…`, so the
 * toolbar opens narrowed to a project. The chip says where the narrowing
 * came from, and clearing it returns to the full list.
 */
export const RegistryFocused: Story = {
  render: () => (
    <Frame>
      <SourcesPage focus="plexor" />
    </Frame>
  ),
}

/**
 * A platform with no connections yet, pinned honestly: the sources cache
 * is seeded with an empty snapshot — no connections, no tickets — before
 * the page mounts. The page answers with its first-run reading, the one
 * that points at the connect flow, because an empty registry is a call to
 * action rather than a blank table.
 */
export const FirstRun: Story = {
  render: () => (
    <Frame
      seed={(client) => {
        client.setQueryData(sourcesQueryKey, {
          connections: [],
          tickets: [],
        })
      }}
    >
      <SourcesPage />
    </Frame>
  ),
}
