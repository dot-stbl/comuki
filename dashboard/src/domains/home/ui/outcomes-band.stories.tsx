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

/**
 * Still coming. It stands where the band will be rather than leaving a hole —
 * a missing band and a swarm that has finished nothing look identical, and
 * only one of them is true.
 */
export const Loading: Story = {
  args: { days: undefined, loading: true },
}

/**
 * The week did not load. Said in the band's own quiet voice and never in the
 * API's: history is this screen's second question, and a red rule here would
 * outrank the verdict above it.
 */
export const DidNotLoad: Story = {
  args: { days: undefined, failed: true },
}
