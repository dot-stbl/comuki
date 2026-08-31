import type { Meta, StoryObj } from "@storybook/react"

import { CostStat } from "./cost-stat"
import { ProxyBudgetMeter } from "./proxy-budget-meter"

const meta: Meta<typeof CostStat> = {
  title: "Cost/Stat",
  component: CostStat,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  // A tile is read beside two others in a grid track, so it is shown at a
  // track's width rather than at the page's — a figure judged against the
  // wrong measure is not the component.
  decorators: [
    (Story) => (
      <div style={{ inlineSize: "18rem" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof CostStat>

/**
 * The plain shape: a data label, a figure in the data voice, and one line of
 * prose saying what the figure means. No fill that lifts it off the floor and
 * no shadow that floats it — the hairline on its start edge is the whole
 * boundary.
 */
export const Reading: Story = {
  args: {
    name: "per-success",
    label: "Cost per success",
    prefix: "$",
    value: "0.42",
    sub: "key business metric — per successful task, not per call",
  },
}

/** A unit rides after the figure instead of before it. */
export const WithAUnit: Story = {
  args: {
    name: "per-day",
    label: "Green gate",
    value: "86",
    suffix: "%",
    sub: "of tasks cleared today",
  },
}

/**
 * The capped reading, two thirds spent. There is nothing to decide here, so
 * there is no hue — which is exactly how the progress bar this replaced looked
 * at the same number.
 */
export const Capped: Story = {
  args: {
    name: "proxy-budget",
    label: "Proxy budget",
    value: "67",
    suffix: "%",
    heat: "ok",
    sub: "$148 / $220 · kill-switch at cap",
    children: <ProxyBudgetMeter budget={{ used: 148.2, cap: 220 }} />,
  },
}

/** Past 85%: a decision — raise the cap, or let the kill-switch stop the swarm. */
export const NearTheCap: Story = {
  args: {
    name: "proxy-budget",
    label: "Proxy budget",
    value: "92",
    suffix: "%",
    heat: "near",
    sub: "$202 / $220 · kill-switch at cap",
    children: <ProxyBudgetMeter budget={{ used: 202, cap: 220 }} />,
  },
}

/** At the cap. The bar cannot grow, so the second channel is a weave. */
export const OverTheCap: Story = {
  args: {
    name: "proxy-budget",
    label: "Proxy budget",
    value: "104",
    suffix: "%",
    heat: "over",
    sub: "$229 / $220 · kill-switch at cap",
    children: <ProxyBudgetMeter budget={{ used: 229, cap: 220 }} />,
  },
}

/**
 * The three readings the screen actually opens with, side by side — the check
 * that a row of tiles reads as one row rather than three unrelated slabs.
 */
export const TheRow: Story = {
  decorators: [
    (Story) => (
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(3, minmax(0, 1fr))",
          gap: "var(--s4)",
          inlineSize: "58rem",
        }}
      >
        <Story />
      </div>
    ),
  ],
  render: () => (
    <>
      <CostStat
        name="per-success"
        label="Cost per success"
        prefix="$"
        value="0.42"
        sub="key business metric — per successful task, not per call"
      />
      <CostStat
        name="per-day"
        label="Per day"
        prefix="$"
        value="148"
        sub="86% of tasks — green gate"
      />
      <CostStat
        name="proxy-budget"
        label="Proxy budget"
        value="67"
        suffix="%"
        heat="ok"
        sub="$148 / $220 · kill-switch at cap"
      >
        <ProxyBudgetMeter budget={{ used: 148.2, cap: 220 }} />
      </CostStat>
    </>
  ),
}
