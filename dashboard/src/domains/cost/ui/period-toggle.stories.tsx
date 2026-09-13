import type { Meta, StoryObj } from "@storybook/react"

import { useState } from "react"

import { PeriodToggle } from "./period-toggle"

const meta: Meta<typeof PeriodToggle> = {
  title: "Cost/Period toggle",
  component: PeriodToggle,
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
type Story = StoryObj<typeof PeriodToggle>

/** A controlled wrapper so the rendered control actually answers to clicks. */
function Controlled() {
  const [value, setValue] = useState<"day" | "week" | "month">("day")
  return (
    <PeriodToggle
      value={value}
      onChange={setValue}
      options={[
        { value: "day", label: "day", note: "1d" },
        { value: "week", label: "week", note: "7d" },
        { value: "month", label: "month", note: "30d" },
      ]}
    />
  )
}

export const Default: Story = {
  render: () => <Controlled />,
}
