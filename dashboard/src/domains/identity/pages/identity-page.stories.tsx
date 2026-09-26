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

import { IdentityPage } from "./identity-page"

/* Who exists, what they hold, what acts for them. Three lists that share a
   screen because none answers its own question.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the three lists ride one payload, joined in the seed store.
   A story that says "three users, six grants, four keys" actually has the
   three lists the operator would see if they had just signed in.

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
    "/identity/users/new",
    "/identity/users/$userId",
    "/identity/users/$userId/link",
    "/identity/grants/new",
    "/identity/keys/new",
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
  history: createMemoryHistory({ initialEntries: ["/identity"] }),
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

const meta: Meta<typeof IdentityPage> = {
  title: "Pages/Identity",
  component: IdentityPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof IdentityPage>

/** The users tab — every account the registry holds, with the platform
 *  role it carries and the project role it does not. The list is the door to
 *  account acts; the rest of the page is the door to what those accounts
 *  hold. */
export const UsersTab: Story = {
  render: () => (
    <Frame>
      <IdentityPage tab="users" onTabChange={() => {}} />
    </Frame>
  ),
}

/** The grants tab — every grant the registry holds, on every project. A
 *  grant is illegible until you see whose it is; the row carries both. */
export const GrantsTab: Story = {
  render: () => (
    <Frame>
      <IdentityPage tab="grants" onTabChange={() => {}} />
    </Frame>
  ),
}

/** The keys tab — every API key the registry holds, with the project it
 *  opens and whether it is still active. A revoked key is still on the
 *  list, in red, for the audit story a deleted key would erase. */
export const KeysTab: Story = {
  render: () => (
    <Frame>
      <IdentityPage tab="keys" onTabChange={() => {}} />
    </Frame>
  ),
}

/** Arrived at the grants list with a focus — the address bar named the
 *  subject. The list opens narrowed, and the toolbar shows why: a chip with
 *  the seed value that says "this is why your list is short". */
export const GrantsFocused: Story = {
  render: () => (
    <Frame>
      <IdentityPage tab="grants" focus="rhea" onTabChange={() => {}} />
    </Frame>
  ),
}
