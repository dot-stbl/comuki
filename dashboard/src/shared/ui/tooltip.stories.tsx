import type { Meta, StoryObj } from "@storybook/react"
import { Gauge, ListOrdered, PlayCircle } from "lucide-react"

import { Button } from "./button"
import { Tooltip } from "./tooltip"

const meta: Meta<typeof Tooltip> = {
  title: "UI Kit/Overlays/Tooltip",
  component: Tooltip,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Tooltip>

/** Hover it, or tab to it: focus opens with no dwell at all. */
export const Default: Story = {
  render: () => (
    <Tooltip content="Live runs">
      <Button variant="ghost" size="icon" aria-label="Live runs">
        <PlayCircle aria-hidden="true" />
      </Button>
    </Tooltip>
  ),
}

/** The collapsed rail's case: the name arrives beside the icon, not above it. */
export const Beside: Story = {
  render: () => (
    <Tooltip content="Queue" placement="end">
      <Button variant="ghost" size="icon" aria-label="Queue">
        <ListOrdered aria-hidden="true" />
      </Button>
    </Tooltip>
  ),
}

/**
 * Off, because the control already says it. The wrapper stays in the tree so
 * the control underneath is never remounted by the switch.
 */
export const Disabled: Story = {
  render: () => (
    <Tooltip content="Attention" disabled>
      <Button variant="ghost">
        <Gauge aria-hidden="true" />
        Attention
      </Button>
    </Tooltip>
  ),
}

/** A longer reading still stays one line and truncates rather than wrapping. */
export const LongReading: Story = {
  render: () => (
    <Tooltip content="Runs waiting on a human, across every project in scope">
      <Button variant="outline">what is this</Button>
    </Tooltip>
  ),
}
