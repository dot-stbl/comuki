import type { Meta, StoryObj } from "@storybook/react"

import { ProxyBudgetMeter } from "./proxy-budget-meter"

const meta: Meta<typeof ProxyBudgetMeter> = {
  title: "Cost/Proxy budget meter",
  component: ProxyBudgetMeter,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ inlineSize: "18rem" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof ProxyBudgetMeter>

/** Room to spare: no hue, because there is nothing here to decide. */
export const Ok: Story = {
  args: { budget: { used: 148.2, cap: 220 } },
}

/** Past 85%: the amber the whole product uses for a wait on a decision. */
export const NearTheCap: Story = {
  args: { budget: { used: 202, cap: 220 } },
}

/**
 * At or past the cap. The bar has run out of length, so the reading moves to a
 * second channel — the product's own failed weave — rather than to more red.
 */
export const OverTheCap: Story = {
  args: { budget: { used: 229, cap: 220 } },
}

/**
 * The three readings in greyscale. `near` and `over` still tell each other
 * apart, because the weave carries what the hue was carrying.
 */
export const InGreyscale: Story = {
  render: () => (
    <div
      style={{
        filter: "grayscale(1)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--s4)",
      }}
    >
      <ProxyBudgetMeter budget={{ used: 148.2, cap: 220 }} />
      <ProxyBudgetMeter budget={{ used: 202, cap: 220 }} />
      <ProxyBudgetMeter budget={{ used: 229, cap: 220 }} />
    </div>
  ),
}

/** No cap at all reads as full, never as empty. */
export const NoCap: Story = {
  args: { budget: { used: 12, cap: 0 } },
}
