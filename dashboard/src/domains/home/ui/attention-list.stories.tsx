import { createContext, useContext, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { groupAttention, readAttention } from "@/domains/home/model/attention"
import { toRunSummary } from "@/domains/runs/api/mappers"
import { PROJECTS_SEED, RUNS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider, type SessionUser } from "@/shared/session"

import { AttentionList } from "./attention-list"

/* The rows carry real `<Link>`s and ask a real permission, so they only render
   inside a router and a session. A memory router with the product's own paths
   gives the links somewhere to go without the app's route tree. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/runs/$runId", "/tasks", "/approvals"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

/** Watches every project and decides on none — every act explains itself. */
const WATCHER: SessionUser = {
  id: "u_watch",
  name: "Watcher",
  email: "watch@comuki.local",
  platformRoles: ["viewer"],
  projectRoles: {},
}

function Frame({
  children,
  user = SESSION_USER_SEED,
}: {
  children: ReactNode
  user?: SessionUser
}) {
  return (
    <SessionProvider user={user} projects={PROJECTS_SEED}>
      <SlotContext value={children}>
        <RouterProvider router={router} />
      </SlotContext>
    </SessionProvider>
  )
}

const shift = readAttention(RUNS_SEED.map(toRunSummary))
const shown = groupAttention(shift.items.slice(0, 12))
const hidden = Math.max(0, shift.items.length - 12)

function List({
  user,
  groups = shown,
  more = hidden,
}: {
  user?: SessionUser
  groups?: typeof shown
  more?: number
}) {
  return (
    <Frame user={user}>
      <AttentionList
        groups={groups}
        hidden={more}
        approvingId={null}
        cancellingId={null}
        onApprove={() => {}}
        onStop={() => {}}
      />
    </Frame>
  )
}

const meta: Meta<typeof AttentionList> = {
  title: "Home/Attention list",
  component: AttentionList,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof AttentionList>

/**
 * The seeded shift, seen by the seeded duty engineer — who approves on one
 * project, administers another and only watches the third. Approve and Stop
 * are live on some rows and explain themselves on others, which is the
 * ordinary case here rather than an edge one.
 */
export const FullShift: Story = {
  render: () => <List />,
}

/** Every act refused. Nothing is hidden; every refusal names what is missing. */
export const NothingDecidable: Story = {
  render: () => <List user={WATCHER} />,
}

/** One bucket. A failed gate offers only the move it actually has: open it. */
export const OnlyFailures: Story = {
  render: () => (
    <List
      groups={shown.filter((group) => group.status === "failed")}
      more={0}
    />
  ),
}

/** Under the cap: no footer, because nothing was left out. */
export const ShortList: Story = {
  render: () => (
    <List groups={groupAttention(shift.items.slice(0, 3))} more={0} />
  ),
}

/** A decision in flight: the row is busy, not denied — the two look different. */
export const Deciding: Story = {
  render: () => (
    <Frame>
      <AttentionList
        groups={groupAttention(shift.items.slice(0, 4))}
        hidden={0}
        approvingId={shift.items[0]?.run.id ?? null}
        cancellingId={null}
        onApprove={() => {}}
        onStop={() => {}}
      />
    </Frame>
  ),
}
