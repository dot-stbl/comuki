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

import type { KnowledgeTab } from "@/domains/knowledge/model/tabs"

import { SessionProvider } from "@/shared/session"

import { KnowledgePage } from "./knowledge-page"

/* What the swarm has been told, and what telling it that did. The page is
   read-only: rules live in the client's git and change by commit. The one
   exception is the verification gate, folded in from the screen that used
   to stand at `/verify`, which carries its own permission.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the library reads the seed store, the gate reads its own
   panel set. A story that says "ten rules, six golden tasks" actually has
   ten rows the operator can click on.

   A tab change inside the story is local — the URL is the router's, and the
   story lives without one — so the page receives the active tab as a prop.
   This is the contract the route itself honours (`?tab=`), and the page
   reads no router state on its own. */

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
  history: createMemoryHistory({ initialEntries: ["/knowledge"] }),
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

const meta: Meta<typeof KnowledgePage> = {
  title: "Pages/Knowledge",
  component: KnowledgePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof KnowledgePage>

function storyOf(tab: KnowledgeTab, focus?: string) {
  return (
    <Frame>
      <KnowledgePage tab={tab} focus={focus} onTabChange={() => {}} />
    </Frame>
  )
}

/** The library — every rule, doc and skill the swarm has been told about,
 *  and the revisions they ride. The list is the answer to "what is in
 *  force"; the search field above narrows it without leaving the screen. */
export const Library: Story = { render: () => storyOf("library") }

/** Arrived at the library with a focus — the address bar said `?q=…`, so
 *  the search opens narrowed to the named rule. The chip says where the
 *  narrowing came from. */
export const LibraryFocused: Story = {
  render: () => storyOf("library", "checkout"),
}

/** The verification gate — folded in from the screen that used to stand at
 *  `/verify`. The same chassis as the library (read-only registry sourced
 *  from the client's git, project-scoped), so it sits behind the same
 *  door with its own tab rather than claiming a share of the page. */
export const Gate: Story = { render: () => storyOf("gate") }
