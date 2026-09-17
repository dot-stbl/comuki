import type { Meta, StoryObj } from "@storybook/react"

import { StatFigure, StatLabel, StatTile } from "./stat-tile"

const meta: Meta<typeof StatTile> = {
  title: "UI Kit/Data/Stat Tile",
  component: StatTile,
  parameters: { layout: "padded" },
  argTypes: {
    tone: { control: "radio", options: ["neutral", "attention", "danger"] },
  },
}

export default meta
type Story = StoryObj<typeof meta>

/** The ordinary reading: a fact about a period that has already happened. */
export const Neutral: Story = {
  args: {
    label: "March spend",
    name: "total",
    prefix: "$",
    value: "1,284.40",
    sub: "burning $41.43/day · 31 days",
  },
}

/** Close to the thing it is capped by. The edge lights and so does the figure. */
export const Attention: Story = {
  args: {
    label: "Forecast 31 Mar",
    name: "forecast",
    prefix: "$",
    value: "198.00",
    tone: "attention",
    sub: "90% of $220 cap · burn rate $6.40/day",
  },
}

/** Past it. Same two channels, one step further, and the sentence says so. */
export const Danger: Story = {
  args: {
    label: "Budget progress",
    name: "budget",
    prefix: "$",
    value: "241",
    suffix: "/ $220",
    tone: "danger",
    sub: "over the cap · new claims blocked",
  },
}

/** Without a sub line — a reading that needs no sentence under it. */
export const FigureOnly: Story = {
  args: { label: "active rules", name: "rules", value: "128" },
}

/**
 * A row of tiles, which is the arrangement the one type step exists for: three
 * readings on one line compare only if the figures are set at one size.
 */
export const Row: Story = {
  render: () => (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(auto-fit, minmax(min(17rem, 100%), 1fr))",
        gap: "var(--s4)",
      }}
    >
      <StatTile label="March spend" prefix="$" value="1,284.40" sub="31 days" />
      <StatTile
        label="Forecast"
        prefix="$"
        value="198.00"
        tone="attention"
        sub="90% of cap"
      />
      <StatTile
        label="Budget"
        prefix="$"
        value="241"
        suffix="/ $220"
        tone="danger"
        sub="over the cap"
      />
    </div>
  ),
}

/**
 * The two pieces on their own: the label voice for anything that names a value,
 * and the figure for a card that already has a header to stand it in.
 */
export const Pieces: Story = {
  render: () => (
    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)" }}>
      <StatLabel>quota</StatLabel>
      <StatFigure value="3" suffix="slots free" />
      <StatFigure value="0" suffix="slots free" tone="danger" />
    </div>
  ),
}
