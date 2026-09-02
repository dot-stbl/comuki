import type { Meta, StoryObj } from "@storybook/react"

import { COST_SEED } from "@/shared/api/mock/cost.seed"
import { toCostSummary } from "@/domains/cost/api/mappers"

import { SpendByDay } from "./spend-by-day"

const meta: Meta<typeof SpendByDay> = {
  title: "Cost/Spend by day",
  component: SpendByDay,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: { days: toCostSummary(COST_SEED).byDay },
}
export default meta
type Story = StoryObj<typeof SpendByDay>

/** The seeded week: weekend columns light, the auth-svc spike three days back. */
export const SeededWeek: Story = {}

/** A week where nothing ran says so rather than drawing a blank box. */
export const QuietWeek: Story = {
  args: { days: [] },
}
