import type { Meta, StoryObj } from "@storybook/react"

import type { WorkItemStatus, WorkerState } from "@/domains/queue/model/types"

import { WorkStatusBadge, WorkerStateBadge } from "./queue-badges"

const STATUSES: WorkItemStatus[] = [
  "blocked",
  "queued",
  "running",
  "succeeded",
  "failed",
  "cancelled",
]

const STATES: WorkerState[] = ["idle", "busy", "draining"]

function Row({ children }: { children: React.ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexWrap: "wrap",
        gap: "var(--s4)",
        alignItems: "center",
        padding: "var(--s6)",
      }}
    >
      {children}
    </div>
  )
}

const meta: Meta<typeof WorkStatusBadge> = {
  title: "Queue/Badges",
  component: WorkStatusBadge,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof WorkStatusBadge>

/**
 * The six work-item statuses. Not the six run statuses — an item `succeeded`
 * where a run `success`es, and `blocked` and `cancelled` have no run
 * equivalent at all, which is why this is a domain badge and not the kit's.
 *
 * There is deliberately no `stalled`: a lapsed lease is an event that becomes
 * `failed` or goes back to `queued`.
 */
export const WorkItemStatuses: Story = {
  render: () => (
    <Row>
      {STATUSES.map((status) => (
        <WorkStatusBadge key={status} status={status} />
      ))}
    </Row>
  ),
}

/**
 * Every value carries a silhouette as well as a hue, so the set survives
 * greyscale — and the two that are owed nothing (`blocked` waits on its own
 * run, `cancelled` is over) are given no hue at all.
 */
export const StatusInGreyscale: Story = {
  render: () => (
    <div style={{ filter: "grayscale(1)" }}>
      <Row>
        {STATUSES.map((status) => (
          <WorkStatusBadge key={status} status={status} />
        ))}
      </Row>
    </div>
  ),
}

/** The pool's three states. Idle is the least saturated: it is not a problem. */
export const WorkerStates: Story = {
  render: () => (
    <Row>
      {STATES.map((state) => (
        <WorkerStateBadge key={state} state={state} />
      ))}
    </Row>
  ),
}
