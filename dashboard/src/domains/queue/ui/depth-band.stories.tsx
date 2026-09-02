import type { Meta, StoryObj } from "@storybook/react"

import { listQueueDepth, resetPool } from "@/domains/queue/api/pool.store"

import { DepthBand } from "./depth-band"

const meta: Meta<typeof DepthBand> = {
  title: "Queue/Depth band",
  component: DepthBand,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: { days: listQueueDepth() },
  loaders: [
    async () => {
      // A story always starts from the seeded shift, not from whatever the
      // previous story's admin acts left in the mutable store.
      resetPool()
      return {}
    },
  ],
}
export default meta
type Story = StoryObj<typeof DepthBand>

/** The seeded week: single digits until today, and today is the spike. */
export const SeededWeek: Story = {}

/** A quiet week where today is nothing special — the sentence drops the note. */
export const OrdinaryWeek: Story = {
  args: {
    days: [
      { label: "mon", depth: 4 },
      { label: "tue", depth: 6 },
      { label: "wed", depth: 5 },
      { label: "thu", depth: 8 },
      { label: "fri", depth: 6 },
      { label: "sat", depth: 9 },
      { label: "today", depth: 3 },
    ],
  },
}

/** An empty series draws no band rather than an empty one. */
export const NoHistory: Story = {
  args: { days: [] },
}
