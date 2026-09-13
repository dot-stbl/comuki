import type { Meta, StoryObj } from "@storybook/react"

import { ProxyBudgetMeter } from "./proxy-budget-meter"
import { ForecastWidget } from "./forecast-widget"

const meta: Meta<typeof ForecastWidget> = {
  title: "Cost/Forecast widget",
  component: ForecastWidget,
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
type Story = StoryObj<typeof ForecastWidget>

/** Comfortably under cap — green, no hue. */
export const Under: Story = {
  args: {
    forecast: {
      cap: 1450,
      burnRatePerDay: 131.11,
      projectedEndOfPeriod: 917.8,
      share: 0.63,
    },
    burnRateLabel: "$131.11 / day",
    projectedLabel: "end of week",
    meter: <ProxyBudgetMeter budget={{ used: 917.8, cap: 1450 }} />,
  },
}

/** Past 85%: amber, the wait that says "raise the cap or stop the swarm". */
export const Near: Story = {
  args: {
    forecast: {
      cap: 480,
      burnRatePerDay: 137.0,
      projectedEndOfPeriod: 438.4,
      share: 0.91,
    },
    burnRateLabel: "$137.00 / day",
    projectedLabel: "end of month",
    meter: <ProxyBudgetMeter budget={{ used: 438.4, cap: 480 }} />,
  },
}

/** Over the cap — the forecast the operator was warned about last week. */
export const Over: Story = {
  args: {
    forecast: {
      cap: 90,
      burnRatePerDay: 13.4,
      projectedEndOfPeriod: 122.7,
      share: 1.36,
    },
    burnRateLabel: "$13.40 / day",
    projectedLabel: "end of month",
    meter: <ProxyBudgetMeter budget={{ used: 90, cap: 90 }} />,
  },
}

/** No cap reads full, never empty — matches the proxy-budget-meter rule. */
export const NoCap: Story = {
  args: {
    forecast: { cap: 0, burnRatePerDay: 0, projectedEndOfPeriod: 0, share: 1 },
    burnRateLabel: "$0 / day",
    projectedLabel: "end of month",
    meter: <ProxyBudgetMeter budget={{ used: 0, cap: 0 }} />,
  },
}
