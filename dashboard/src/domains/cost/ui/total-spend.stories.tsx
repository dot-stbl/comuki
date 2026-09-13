import type { Meta, StoryObj } from "@storybook/react"

import { TotalSpend } from "./total-spend"

const meta: Meta<typeof TotalSpend> = {
  title: "Cost/Total spend",
  component: TotalSpend,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ inlineSize: "22rem" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof TotalSpend>

export const Up: Story = {
  args: {
    total: 917.8,
    previousTotal: 862.4,
    periodLabel: "this week",
    burnNote: "$131.11 / day average",
  },
}

export const Down: Story = {
  args: {
    total: 148.2,
    previousTotal: 152.7,
    periodLabel: "today",
    burnNote: "vs yesterday: -$4.50",
  },
}

export const NoPrior: Story = {
  args: {
    total: 12.4,
    previousTotal: 0,
    periodLabel: "today",
    burnNote: "first reading of the day",
  },
}
