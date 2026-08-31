import type { Meta, StoryObj } from "@storybook/react"

import type { CostByApp } from "@/domains/cost/model/types"

import { SpendByApp } from "./spend-by-app"

function app(name: string, spend: number): CostByApp {
  return { app: name, spend, runs: 1, perSuccess: spend, trend: "+0%" }
}

const DAY: CostByApp[] = [
  app("billing-api", 52.4),
  app("web-app", 41.1),
  app("auth-svc", 33.8),
  app("worker-pool", 14.2),
  app("docs-site", 6.7),
]

const meta: Meta<typeof SpendByApp> = {
  title: "Cost/Spend by app",
  component: SpendByApp,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ inlineSize: "28rem" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof SpendByApp>

/**
 * A day's spend, ranked. Every bar is measured against the largest — bars on
 * their own scales cannot be compared, and comparing them is the whole task.
 */
export const ADay: Story = {
  args: { rows: DAY },
}

/**
 * The bars are neutral at every length, which is the one decision here the old
 * screen made differently: it painted them in the accent. Saturation in this
 * product is reserved for status inside the flow, and being third in a spend
 * ranking is not a status — nobody is being asked to do anything about it.
 * Length is what ranks, and length survives.
 */
export const InGreyscale: Story = {
  render: () => (
    <div style={{ filter: "grayscale(1)" }}>
      <SpendByApp rows={DAY} />
    </div>
  ),
}

/**
 * A quiet day. The axis is floored at a dollar, so five cents across five apps
 * draws five short bars rather than one full one — which is the honest picture
 * of a day where almost nothing ran.
 */
export const AQuietDay: Story = {
  args: {
    rows: [app("billing-api", 0.41), app("web-app", 0.12), app("docs-site", 0.03)],
  },
}

/** One app carrying the whole day, and the rest nowhere near it. */
export const OneRunawayApp: Story = {
  args: {
    rows: [app("auth-svc", 214.9), app("web-app", 8.2), app("docs-site", 1.4)],
  },
}

/** Nothing ran. Said in a word rather than left as an empty box. */
export const Nothing: Story = {
  args: { rows: [] },
}
