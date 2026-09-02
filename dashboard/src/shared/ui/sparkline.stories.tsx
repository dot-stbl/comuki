import type { Meta, StoryObj } from "@storybook/react"

import { Sparkline } from "./sparkline"

const meta: Meta<typeof Sparkline> = {
  title: "UI Kit/Data/Sparkline",
  component: Sparkline,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: {
    values: [
      0.31, 0.22, 0.14, 0.11, 0.08, 0.09, 0.16, 0.44, 1.03, 1.6, 2.1, 1.85,
      1.44, 1.76, 2.32, 2.68, 3.42, 2.71, 2.36, 2.24, 1.87, 1.42, 0.83, 0.52,
    ],
    label:
      "Spend by hour across the metered day: $31.40 total, peak $3.42 at 16:00.",
  },
}
export default meta
type Story = StoryObj<typeof Sparkline>

/** A metered day: quiet night, morning ramp, heavy afternoon, evening taper. */
export const BurnByHour: Story = {}

/** A flat series stays flat — the scale is zero-to-max, never min-to-max. */
export const SteadyDay: Story = {
  args: {
    values: Array.from({ length: 24 }, () => 1.2),
    label: "Spend by hour: steady at about $1.20 all day.",
  },
}

/** One value has no shape; the sentence still says the reading. */
export const SinglePoint: Story = {
  args: {
    values: [2.4],
    label: "Spend by hour: $2.40 so far today.",
  },
}

/** A day nothing was metered draws no line and keeps its name. */
export const NothingMetered: Story = {
  args: {
    values: Array.from({ length: 24 }, () => 0),
    label: "Spend by hour: nothing metered.",
  },
}
