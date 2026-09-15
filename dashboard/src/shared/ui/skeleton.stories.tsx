import type { Meta, StoryObj } from "@storybook/react"

import { Skeleton } from "./skeleton"

const meta = {
  title: "UI Kit/Feedback/Skeleton",
  component: Skeleton,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  argTypes: {
    inset: {
      control: "inline-radio",
      options: ["flush", "gutter", "page", "none"],
    },
    fill: { control: "boolean" },
    label: { control: "text" },
  },
} satisfies Meta<typeof Skeleton>

export default meta
type Story = StoryObj<typeof meta>

/** Four bars at the kit's own rhythm — the default, and the commonest screen. */
export const Default: Story = { args: {} }

/** A longer list. The rhythm carries to nine before it would repeat. */
export const Nine: Story = { args: { lines: 9 } }

/** The widths a screen already keeps beside its page component. */
export const OwnWidths: Story = {
  args: { lines: ["58%", "42%", "71%", "50%"] },
}

/** Inside a screen body that has not paid for its own gutter. */
export const Gutter: Story = { args: { lines: 5, inset: "gutter" } }

/** Growing into the room the table will take, so nothing jumps on arrival. */
export const Fill: Story = {
  args: { lines: 6, fill: true },
  render: (args) => (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        blockSize: "18rem",
        border: "var(--hairline) solid var(--rule)",
        borderRadius: "var(--r-lg)",
      }}
    >
      <Skeleton {...args} />
    </div>
  ),
}
