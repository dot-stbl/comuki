import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { PROVIDERS } from "@/domains/sources/model/providers"
import type { ProviderKey } from "@/domains/sources/model/types"
import type { TaskPriority, TaskStatus } from "@/domains/tasks/model/types"

import {
  TaskPriorityBadge,
  TaskSourceBadge,
  TaskStatusBadge,
} from "./tasks-badges"

/* Every provider in the registry, plus one that is not in it — the badge has
   to survive a word the dashboard has never met, and a story nobody can see
   it in is a story that will not catch it going away. */
const SOURCES: ProviderKey[] = [
  ...PROVIDERS.map((provider) => provider.key),
  "linear",
]
const PRIORITIES: TaskPriority[] = ["high", "normal", "low"]
const STATUSES: TaskStatus[] = ["new", "queued", "planning"]

function Row({ children }: { children: ReactNode }) {
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

const meta: Meta<typeof TaskStatusBadge> = {
  title: "Tasks/Badges",
  component: TaskStatusBadge,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof TaskStatusBadge>

/**
 * Where the ticket came from. A ticket off a branch shows the tracker's own id
 * — for that row this badge *is* the identity — and native intake has only the
 * word, which is the honest limit of a column that is a badge.
 *
 * The last stamp is a provider with no registry entry: no mark to draw, so it
 * takes the board glyph, and it still carries its id rather than a blank.
 */
export const Sources: Story = {
  render: () => (
    <Row>
      {SOURCES.map((source) => (
        <TaskSourceBadge key={source} source={source} id="COMUKI-128" />
      ))}
    </Row>
  ),
}

/**
 * Urgency. `normal` is deliberately given no hue at all: a backlog where two
 * thirds of the rows are tinted has taught the operator to stop reading tint.
 */
export const Priorities: Story = {
  render: () => (
    <Row>
      {PRIORITIES.map((priority) => (
        <TaskPriorityBadge key={priority} priority={priority} />
      ))}
    </Row>
  ),
}

/** Intake order — not a run's status, which is why this is not the kit badge. */
export const Statuses: Story = {
  render: () => (
    <Row>
      {STATUSES.map((status) => (
        <TaskStatusBadge key={status} status={status} />
      ))}
    </Row>
  ),
}

/**
 * Every mark carries a silhouette as well as a hue, so the set survives
 * greyscale. This is the story the priority mark was failing before it had an
 * icon: `high` and `normal` were a coral wash and a grey one, and in a column
 * whose whole job is to be scanned they were the same shape.
 */
export const InGreyscale: Story = {
  render: () => (
    <div style={{ filter: "grayscale(1)" }}>
      <Row>
        {PRIORITIES.map((priority) => (
          <TaskPriorityBadge key={priority} priority={priority} />
        ))}
        {STATUSES.map((status) => (
          <TaskStatusBadge key={status} status={status} />
        ))}
      </Row>
    </div>
  ),
}
