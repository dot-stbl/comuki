import type { Meta, StoryObj } from "@storybook/react"

import { Label } from "@/shared/ui/label"

const meta: Meta<typeof Label> = {
  title: "UI/Label",
  component: Label,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Label>

export const Default: Story = {
  args: { children: "Label text" },
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => <Label className="opacity-50">Disabled label</Label>,
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  args: {
    children:
      "This is a very long label text that demonstrates how the component handles extended content without breaking the layout or causing any overflow issues",
  },
}