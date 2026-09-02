import type { Meta, StoryObj } from "@storybook/react"

import { BarSeries, type BarSeriesPoint } from "./bar-series"

const meta: Meta<typeof BarSeries> = {
  title: "UI Kit/Data/BarSeries",
  component: BarSeries,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: {
    label:
      "Spend by day: $965 over the last 7 days, $138 a day on average; heaviest thu at $181.",
  },
}
export default meta
type Story = StoryObj<typeof BarSeries>

/** One neutral value per day — the spend and queue-depth shape. */
const week: BarSeriesPoint[] = [
  { key: "6", label: "sat", segments: [{ value: 96.5 }] },
  { key: "5", label: "sun", segments: [{ value: 88.2 }] },
  { key: "4", label: "mon", segments: [{ value: 130.4 }] },
  { key: "3", label: "tue", segments: [{ value: 180.8 }] },
  { key: "2", label: "wed", segments: [{ value: 131.1 }] },
  { key: "1", label: "thu", segments: [{ value: 142.6 }] },
  { key: "0", label: "today", segments: [{ value: 148.2 }] },
]

export const WeekOfSpend: Story = {
  args: { points: week },
}

/** Stacked by outcome, worst on top — the home reading. */
export const Outcomes: Story = {
  args: {
    points: week.map((point, index) => ({
      ...point,
      segments: [
        { value: 24 + index, status: "success" as const },
        { value: 3 + (index % 3), status: "failed" as const },
        { value: index % 4, status: "escalated" as const },
      ],
    })),
    label:
      "Run outcomes by day: 47 finished today so far — 26 success, 12 failed, 9 escalated.",
  },
}

/** A day nothing ran draws no bar, and keeps its tick. */
export const QuietDay: Story = {
  args: {
    points: week.map((point, index) => ({
      ...point,
      segments: [{ value: index === 2 ? 0 : point.segments[0]?.value ?? 0 }],
    })),
    label: "Spend by day: nothing ran on monday.",
  },
}

/** A series with nothing in it is a shape that says so, not a blank box. */
export const Empty: Story = {
  args: {
    points: week.map((point) => ({ ...point, segments: [{ value: 0 }] })),
    label: "Spend by day: nothing spent this week.",
  },
}
