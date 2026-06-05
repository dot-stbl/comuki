import type { Meta, StoryObj } from "@storybook/react"

import { Button } from "@/components/ui/button"

const meta: Meta<typeof Button> = {
  title: "UI/Button",
  component: Button,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Button>

export const Default: Story = {
  args: { children: "Get started" },
}

export const Loading: Story = {
  args: { children: "Loading...", disabled: true },
}

export const Disabled: Story = {
  args: { children: "Disabled button", disabled: true },
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  args: {
    children:
      "This is a very long button label that demonstrates how the component handles extended text without breaking the layout",
  },
}