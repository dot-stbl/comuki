import type { Meta, StoryObj } from "@storybook/react"

import { toOutcomeDays } from "@/domains/home/model/outcomes"
import { OUTCOMES_SEED } from "@/shared/api/mock/runs.seed"

import { OutcomesBand } from "./outcomes-band"

const meta: Meta<typeof OutcomesBand> = {
  title: "Home/Outcomes band",
  component: OutcomesBand,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: { days: toOutcomeDays(OUTCOMES_SEED) },
}
export default meta
type Story = StoryObj<typeof OutcomesBand>

/** The seeded week: weekend columns light, the incident spike three days back. */
export const SeededWeek: Story = {}

/** A week with nothing behind it draws no band rather than an empty one. */
export const NoHistory: Story = {
  args: { days: [] },
}
