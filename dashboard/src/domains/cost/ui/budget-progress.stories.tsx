import type { Meta, StoryObj } from "@storybook/react"

import { BudgetProgress } from "./budget-progress"

const meta: Meta<typeof BudgetProgress> = {
  title: "Cost/Budget progress",
  component: BudgetProgress,
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
type Story = StoryObj<typeof BudgetProgress>

/** Comfortable margin on both — green, no hue. */
export const Under: Story = {
  args: {
    todayBurn: 148.2,
    todayCap: 220,
    monthToDate: 917.8,
    monthCap: 1450,
  },
}

/** Today sits at the cap's amber shoulder — the day the kill-switch becomes
 *  a conversation rather than a footnote. */
export const Near: Story = {
  args: {
    todayBurn: 202,
    todayCap: 220,
    monthToDate: 1390,
    monthCap: 1450,
  },
}

/** Today is over; the month is still inside. The two readings are honest
 *  about what each one says. */
export const Over: Story = {
  args: {
    todayBurn: 229,
    todayCap: 220,
    monthToDate: 1380,
    monthCap: 1450,
  },
}
