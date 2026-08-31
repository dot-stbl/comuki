import type { Meta, StoryObj } from "@storybook/react"

import type { Budgets } from "@/domains/settings/model/types"

import { BudgetMeter } from "./budget-meter"

function budgets(patch: Partial<Budgets>): Budgets {
  return {
    perTaskUsd: 2,
    perAppUsd: 40,
    globalUsd: 220,
    usedUsd: 148.2,
    killSwitch: false,
    pauseSwarm: false,
    ...patch,
  }
}

const meta: Meta<typeof BudgetMeter> = {
  title: "Settings/Budget meter",
  component: BudgetMeter,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof BudgetMeter>

/**
 * The ordinary reading. Below the `near` threshold there is nothing to decide,
 * so the fill is drawn in the faint ramp and nothing on the panel is coloured —
 * a bar that changed hue at 40% would have taught the operator to ignore hue by
 * the time one reached 90%.
 *
 * The figures are the reading and the bar is drawn on top of them: nothing here
 * is announced only as a length.
 */
export const Ok: Story = {
  args: { budgets: budgets({ usedUsd: 148.2 }) },
}

/** Past 85%: raising the cap is still a decision rather than an incident. */
export const Near: Story = {
  args: { budgets: budgets({ usedUsd: 200 }) },
}

/** At the cap. The kill-switch fires here, so the reading says `over the cap`. */
export const Over: Story = {
  args: { budgets: budgets({ usedUsd: 236 }) },
}

/**
 * Already stopped. The length stays, because the spend is real; the fill is
 * hatched, because a solid bar claims the number is still moving and with new
 * claims blocked it is not.
 */
export const KillSwitchThrown: Story = {
  args: { budgets: budgets({ usedUsd: 236, killSwitch: true }) },
}

/**
 * An unbudgeted proxy. No cap is not a cap of zero — a full bar would say the
 * swarm had spent everything it was allowed, which is the opposite of true.
 */
export const NoCap: Story = {
  args: { budgets: budgets({ usedUsd: 500, globalUsd: 0 }) },
}
