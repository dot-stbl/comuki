import type { Meta, StoryObj } from "@storybook/react"

import { workerCounts } from "@/domains/queue/model/queue"
import { WORKERS_SEED } from "@/shared/api/mock/queue.seed"
import { toWorker } from "@/domains/queue/api/mappers"

import { PoolStrip } from "./pool-strip"

const seeded = workerCounts(WORKERS_SEED.map(toWorker))

function Strip({ counts }: { counts: typeof seeded }) {
  return (
    <div style={{ height: "var(--h-strip)" }}>
      <PoolStrip counts={counts} onExpand={() => {}} />
    </div>
  )
}

const meta: Meta<typeof PoolStrip> = {
  title: "Queue/Pool strip",
  component: PoolStrip,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof PoolStrip>

/** The seeded shift, collapsed: six busy, two draining, three idle. */
export const Seeded: Story = {
  render: () => <Strip counts={seeded} />,
}

/** Saturated — no spare capacity, and the shape says so without a number. */
export const Saturated: Story = {
  render: () => (
    <Strip counts={{ total: 12, busy: 12, draining: 0, idle: 0 }} />
  ),
}

/** A rolling image swap: most of the pool is leaving, which is worth noticing. */
export const MostlyDraining: Story = {
  render: () => (
    <Strip counts={{ total: 12, busy: 2, draining: 9, idle: 1 }} />
  ),
}

/** No workers at all. The channel is drawn and simply empty — which is a
 *  reading in itself, and the strip's accessible name says it in words. */
export const Empty: Story = {
  render: () => <Strip counts={{ total: 0, busy: 0, draining: 0, idle: 0 }} />,
}
