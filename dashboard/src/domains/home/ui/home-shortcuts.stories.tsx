import { createContext, useContext, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider, type SessionUser } from "@/shared/session"

import { HomeShortcuts } from "./home-shortcuts"

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/tasks", "/approvals"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

const WATCHER: SessionUser = {
  id: "u_watch",
  name: "Watcher",
  email: "watch@comuki.local",
  platformRoles: ["viewer"],
  projectRoles: {},
}

function Frame({ user }: { user: SessionUser }) {
  return (
    <SessionProvider user={user} projects={PROJECTS_SEED}>
      <SlotContext value={<HomeShortcuts />}>
        <RouterProvider router={router} />
      </SlotContext>
    </SessionProvider>
  )
}

const meta: Meta<typeof HomeShortcuts> = {
  title: "Home/Shortcuts",
  component: HomeShortcuts,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof HomeShortcuts>

/** The duty engineer: intake, the duty list and the approval queue. */
export const DutyEngineer: Story = {
  render: () => <Frame user={SESSION_USER_SEED} />,
}

/**
 * A viewer. Navigation a role cannot use is *removed* rather than explained —
 * the opposite of how an action behaves — so two of the three shortcuts go,
 * exactly as the rail has already dropped their destinations.
 */
export const Viewer: Story = {
  render: () => <Frame user={WATCHER} />,
}
