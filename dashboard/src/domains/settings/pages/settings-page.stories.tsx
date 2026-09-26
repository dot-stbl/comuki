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

import type { SettingsTab } from "@/domains/settings/model/tabs"

import { SessionProvider } from "@/shared/session"

import { SettingsPage } from "./settings-page"

/* The control plane, in seven sections. The page is the door the platform
   admin walks through to read or change one of them; nothing on the screen
   claims to be a destination on its own.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the seven panels ride the control-plane snapshot. Three are
   read-only and say so on their own title — apps, rules, tracker — because
   their source is the client's git and changes by commit. The other four
   are live settings and write through the page's own mutation.

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
  history: createMemoryHistory({ initialEntries: ["/settings"] }),
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

const meta: Meta<typeof SettingsPage> = {
  title: "Pages/Settings",
  component: SettingsPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof SettingsPage>

function storyOf(tab: SettingsTab) {
  return (
    <Frame>
      <SettingsPage tab={tab} onTabChange={() => {}} />
    </Frame>
  )
}

/** Apps — read-only. The source is the client's git, and the section
 *  explains that on its own title rather than offering controls that
 *  cannot land. */
export const Apps: Story = { render: () => storyOf("apps") }

/** Rules — read-only, same reason as apps. The swarm rules live in git and
 *  change by commit; this screen is the registry, not the editor. */
export const Rules: Story = { render: () => storyOf("rules") }

/** Autonomy — the live reading: which classes of act the swarm may take
 *  without a human in the loop, and which still ask. The matrix is the
 *  answer to a question somebody else will ask, and the table names it. */
export const Autonomy: Story = { render: () => storyOf("autonomy") }

/** Routing — the role → model map the orchestrator resolves a request
 *  against. The form is one of the screen's two writers, and saving here
 *  changes the next run that lands, not the current one. */
export const Routing: Story = { render: () => storyOf("routing") }

/** Budgets — the per-task, per-app, and global caps, and the two stops
 *  (kill-switch, pause) that act the moment they are pressed. The stops
 *  are not form fields on purpose: an emergency brake that lives behind
 *  a save button is the shape this screen just lost. */
export const Budgets: Story = { render: () => storyOf("budgets") }

/** Keys — read-only. The provider keys come from env, rotation runs inside
 *  the proxy, and the panel names the sources rather than offering a save
 *  that would have nowhere to land. */
export const Keys: Story = { render: () => storyOf("keys") }

/** Tracker — read-only. The project sources and their state, what the
 *  swarm has been told to watch. The settings page is where the operator
 *  checks it; editing a tracker is a sources act on a different screen. */
export const Tracker: Story = { render: () => storyOf("tracker") }
