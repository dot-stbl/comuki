import type { Meta, StoryObj } from "@storybook/react"

import type { CostFailure } from "@/domains/cost/model/types"

import { FailureAnalytics } from "./failure-analytics"

const ROWS: CostFailure[] = [
  { profile: "planner", rate: 0.11, note: "types mismatch most often" },
  { profile: "tester", rate: 0.07, note: "flaky e2e on CI" },
  { profile: "implementer", rate: 0.04, note: "escalates to lead" },
]

const meta: Meta<typeof FailureAnalytics> = {
  title: "Cost/Failure analytics",
  component: FailureAnalytics,
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
type Story = StoryObj<typeof FailureAnalytics>

/**
 * Where the swarm breaks, by profile. A rate with no note is a number nobody
 * can act on, so every row carries the sentence as well.
 */
export const WhereItBreaks: Story = {
  args: { rows: ROWS },
}

/**
 * In greyscale the rates still read: the hue is the column's, and the weight
 * beside the profile name is the channel that survives.
 */
export const InGreyscale: Story = {
  render: () => (
    <div style={{ filter: "grayscale(1)" }}>
      <FailureAnalytics rows={ROWS} />
    </div>
  ),
}

/** A note long enough to run out of row. It truncates; the row never grows. */
export const ALongNote: Story = {
  args: {
    rows: [
      {
        profile: "planner",
        rate: 0.19,
        note: "the generated types drift from the contract whenever the orchestrator ships a migration ahead of the sdk, and the worker only finds out at the type gate",
      },
      ...ROWS.slice(1),
    ],
  },
}

/** A clean day. Said in a word rather than left as an empty box. */
export const NothingFailed: Story = {
  args: { rows: [] },
}
