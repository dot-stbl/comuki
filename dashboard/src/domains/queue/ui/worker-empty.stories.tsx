import type { Meta, StoryObj } from "@storybook/react"

import { WorkerEmpty } from "./worker-empty"

const meta: Meta<typeof WorkerEmpty> = {
  title: "Queue/Empty pool",
  component: WorkerEmpty,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof WorkerEmpty>

/**
 * The screen's hardest state, and the reason it is four states rather than one
 * blank band: an empty pool is *usually correct*, and the day it is not has to
 * look different from the day it is.
 */

/** `min idle = 0`, and there is work waiting. Scale is about to raise one. */
export const Backlog: Story = {
  args: {
    kind: "backlog",
    backlog: 4,
    minIdle: 0,
    poolSize: 0,
    projectKey: "atlas",
  },
}

/** `min idle = 0` and nothing queued. This is the configured resting state. */
export const AtRest: Story = {
  args: {
    kind: "at-rest",
    backlog: 0,
    minIdle: 0,
    poolSize: 0,
    projectKey: "atlas",
  },
}

/** The one empty pool that is actually wrong: a target of two, and none up. */
export const UnderTarget: Story = {
  args: {
    kind: "under-target",
    backlog: 6,
    minIdle: 2,
    poolSize: 0,
    projectKey: "comuki",
  },
}

/** The pool is fine; the toolbar is what emptied the table. */
export const Filtered: Story = {
  args: {
    kind: "filtered",
    backlog: 2,
    minIdle: 2,
    poolSize: 11,
    onClearFilters: () => {},
  },
}
