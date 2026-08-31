import { createContext, useContext, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { readAttention } from "@/domains/home/model/attention"
import { toRunSummary } from "@/domains/runs/api/mappers"
import { PROJECTS_SEED, RUNS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider } from "@/shared/session"

import { RunningNow } from "./running-now"

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/runs", "/runs/$runId"].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/"] }),
})

function Frame({ children }: { children: ReactNode }) {
  return (
    <SessionProvider user={SESSION_USER_SEED} projects={PROJECTS_SEED}>
      <SlotContext value={children}>
        <RouterProvider router={router} />
      </SlotContext>
    </SessionProvider>
  )
}

const shift = readAttention(RUNS_SEED.map(toRunSummary))

const meta: Meta<typeof RunningNow> = {
  title: "Home/Running now",
  component: RunningNow,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RunningNow>

/** A full shift: eight rows, longest in step first, and the rest named. */
export const Busy: Story = {
  render: () => (
    <Frame>
      <RunningNow
        runs={shift.running.slice(0, 8)}
        total={shift.running.length}
      />
    </Frame>
  ),
}

/** Everything in flight fits: no footer, because nothing was left out. */
export const FewRuns: Story = {
  render: () => (
    <Frame>
      <RunningNow runs={shift.running.slice(0, 3)} total={3} />
    </Frame>
  ),
}

/**
 * Nothing in flight. One quiet line — this block never has to be loud, because
 * the block above it has already answered whether anyone is needed.
 */
export const Idle: Story = {
  render: () => (
    <Frame>
      <RunningNow runs={[]} total={0} />
    </Frame>
  ),
}
