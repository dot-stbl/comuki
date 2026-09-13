import type { Meta, StoryObj } from "@storybook/react"

import { SpendByModel } from "./spend-by-model"

const meta: Meta<typeof SpendByModel> = {
  title: "Cost/Spend by model",
  component: SpendByModel,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ inlineSize: "32rem" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof SpendByModel>

/** A realistic day — glm-5.2 is the headline figure, glm-4.5 is the
 *  workhorse, MiniMax-M3 is the specialist, and Claude handles odd jobs. */
export const Day: Story = {
  args: {
    rows: [
      { model: "glm-5.2", spend: 86.2, tokens: 4_650_000, runs: 18 },
      { model: "glm-4.5", spend: 48.9, tokens: 8_120_000, runs: 41 },
      { model: "MiniMax-M3", spend: 11.7, tokens: 1_980_000, runs: 12 },
      { model: "claude-3.5-sonnet", spend: 1.4, tokens: 220_000, runs: 4 },
    ],
  },
}

/** No model spend yet — the empty state, not a blank list. */
export const Empty: Story = {
  args: { rows: [] },
}
