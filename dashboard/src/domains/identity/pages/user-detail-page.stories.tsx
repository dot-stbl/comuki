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

import { identityQueryKey } from "@/domains/identity/api/queries"
import { buildIdentitySnapshot } from "@/domains/identity/model/identity"
import {
  listSeedApiKeys,
  listSeedRoleAssignments,
  listSeedUsers,
  resetSeedIdentity,
  revokeSeedRole,
} from "@/shared/api/mock/identity.store"
import { listSeedProjects } from "@/shared/api/mock/projects.store"
import { SessionProvider } from "@/shared/session"

import { UserDetailPage } from "./user-detail-page"

/* The crumbs, the hand-offs and the link act are all real router destinations,
   and the shell renders the whole rail — so this screen only exists inside a
   router, a session and a query client, the same three the app hands it. A
   memory router carrying the product's own paths gives the story working links
   without dragging in the generated route tree. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const identity = createRoute({
  getParentRoute: () => rootRoute,
  path: "/identity",
  validateSearch: (search: Record<string, unknown>) => search,
  component: blank,
})

const routeTree = rootRoute.addChildren([
  ...[
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
    "/projects",
    "/compute",
    "/models",
    "/observability",
    "/components",
    "/identity/users/new",
    "/identity/users/$userId",
    "/identity/users/$userId/link",
    "/identity/grants/new",
    "/identity/keys/new",
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  ),
  identity,
])

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/identity"] }),
})

/**
 * The platform as the mutable store currently holds it — the same join the
 * query performs, built from the same four lists.
 *
 * The story seeds the cache with it rather than letting the query fetch,
 * because a story that depends on an environment variable being set is a story
 * that photographs an error state on somebody else's machine. The store is
 * still the source, so the one act on this page writes through and comes back
 * consistent instead of swapping the reading out from under itself.
 */
function platform() {
  return buildIdentitySnapshot(
    listSeedUsers(),
    listSeedRoleAssignments(),
    listSeedApiKeys(),
    listSeedProjects(),
    new Date("2026-08-31T09:00:00Z")
  )
}

function Frame({
  setup,
  children,
}: {
  setup?: () => void
  children: ReactNode
}) {
  /* In a state initialiser rather than an effect: the query client has to
     already hold the reading when the page first renders, and an effect runs
     after that. */
  const [client] = useState(() => {
    resetSeedIdentity()
    setup?.()
    const created = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity } },
    })
    created.setQueryData(identityQueryKey, platform())
    return created
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
      projects={[
        { id: "p_comuki", key: "comuki", name: "Comuki platform" },
        { id: "p_atlas", key: "atlas", name: "Atlas" },
      ]}
    >
      <QueryClientProvider client={client}>
        <SlotContext value={children}>
          <RouterProvider router={router} />
        </SlotContext>
      </QueryClientProvider>
    </SessionProvider>
  )
}

/**
 * One person, and the two questions an administrator arrives with: can they
 * get in, and what do they hold.
 *
 * The page shows only what is knowable about a single account — the provider
 * subject, whether it has ever been used, and the roles it holds and where.
 * Everything else is handed to the list that owns it with a filter already
 * applied, because a detail page links to the real screens rather than
 * redrawing their tables.
 */
const meta: Meta<typeof UserDetailPage> = {
  title: "Identity/Person",
  component: UserDetailPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof UserDetailPage>

/**
 * The live reading: an account holding roles on two projects and none on the
 * platform, with no provider subject written yet — so the oidc fact carries
 * the act that is missing rather than an apology.
 */
export const LiveReading: Story = {
  render: () => (
    <Frame>
      <UserDetailPage userId="u_nadia" />
    </Frame>
  ),
}

/**
 * Switched off, grant intact. Disabling somebody and un-granting them are
 * different acts, and this is the screen that has to show both facts at once:
 * the assignment is real, it is inert, and turning the account back on
 * restores it exactly.
 */
export const DisabledStillHolding: Story = {
  render: () => (
    <Frame>
      <UserDetailPage userId="u_tomas" />
    </Frame>
  ),
}

/**
 * Invited and never arrived: no subject, and `never` where a last-seen date
 * would be. `never` is an answer; a blank is a broken render.
 */
export const Invited: Story = {
  render: () => (
    <Frame>
      <UserDetailPage userId="u_ines" />
    </Frame>
  ),
}

/**
 * The empty reading — an account that holds nothing anywhere.
 *
 * The seed has no such person: Inés arrives holding exactly one viewer grant
 * on atlas, so this story takes it away. That is worth saying out loud rather
 * than papering over, because the state is common in life and absent from the
 * fixtures — an account can exist, sign in, and see nothing until somebody
 * grants it something, and the region says so in words instead of rendering an
 * empty box.
 */
export const HoldsNothing: Story = {
  render: () => (
    <Frame setup={() => revokeSeedRole("g_ines_atlas")}>
      <UserDetailPage userId="u_ines" />
    </Frame>
  ),
}

/**
 * An id that resolves to nothing. A stale tab and an old link are the ordinary
 * ways to arrive here, so the state names the id it was given rather than
 * saying "not found" and leaving the reader to guess which link was wrong.
 */
export const NoSuchAccount: Story = {
  render: () => (
    <Frame>
      <UserDetailPage userId="u_gone" />
    </Frame>
  ),
}
