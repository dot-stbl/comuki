import type { Meta, StoryObj } from "@storybook/react"

import { Surface } from "./surface"

const meta = {
  title: "UI Kit/Surfaces/Surface",
  component: Surface,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  argTypes: {
    bound: { control: "inline-radio", options: ["all", "start"] },
    tone: {
      control: "inline-radio",
      options: ["neutral", "attention", "danger"],
    },
    spacing: { control: "inline-radio", options: ["default", "roomy"] },
    as: {
      control: "inline-radio",
      options: ["div", "article", "section", "li"],
    },
  },
} satisfies Meta<typeof Surface>

export default meta
type Story = StoryObj<typeof meta>

const body = (
  <>
    <p
      style={{
        margin: 0,
        fontFamily: "var(--font-ui)",
        fontSize: "var(--t-micro)",
        fontWeight: "var(--fw-semibold)",
        letterSpacing: "var(--tracking-data)",
        color: "var(--text-faint)",
      }}
    >
      plexor · eu-west
    </p>
    <p
      style={{
        margin: 0,
        fontFamily: "var(--font-ui)",
        fontSize: "var(--t-sm)",
        color: "var(--text-muted)",
      }}
    >
      12 of 16 workers up, min idle 2.
    </p>
  </>
)

/** Four sides. One block among several, where the rule tells them apart. */
export const Bounded: Story = { args: { children: body } }

/** The start edge alone. A block that owns its column. */
export const StartEdge: Story = { args: { bound: "start", children: body } }

/** The accent channel, read off the start edge down a stack of tiles. */
export const Tones: Story = {
  args: { children: body },
  render: () => (
    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)" }}>
      <Surface bound="start">{body}</Surface>
      <Surface bound="start" tone="attention">
        {body}
      </Surface>
      <Surface bound="start" tone="danger">
        {body}
      </Surface>
    </div>
  ),
}

/** A panel whose contents are sections rather than lines. */
export const Roomy: Story = {
  args: { bound: "start", spacing: "roomy", children: body },
}

/** One decision in a queue is an `article`, not a `div`. */
export const AsArticle: Story = {
  args: { as: "article", children: body },
}
