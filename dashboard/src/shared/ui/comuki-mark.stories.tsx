import type { Meta, StoryObj } from "@storybook/react"

import { ComukiMark } from "./comuki-mark"

const meta: Meta<typeof ComukiMark> = {
  title: "UI Kit/Brand/ComukiMark",
  component: ComukiMark,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ComukiMark>

/** Topbar size: 16px tall, width follows the artwork. */
export const Default: Story = {
  args: { style: { height: "1rem", width: "auto" } },
}

/** The mark takes `currentColor`, so it belongs to whatever it sits in. */
export const OnAccent: Story = {
  args: {
    style: { height: "2rem", width: "auto", color: "var(--primary)" },
  },
}

/** Down at favicon scale the face detail closes up — see `public/favicon.svg`. */
export const AtFaviconScale: Story = {
  render: () => (
    <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
      {["0.75rem", "1rem", "1.5rem", "3rem"].map((size) => (
        <ComukiMark key={size} style={{ height: size, width: "auto" }} />
      ))}
    </div>
  ),
}
