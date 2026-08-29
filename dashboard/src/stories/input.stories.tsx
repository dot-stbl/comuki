import type { Meta, StoryObj } from "@storybook/react"

import { Input } from "@/shared/ui/input"

const meta: Meta<typeof Input> = {
  title: "UI/Input",
  component: Input,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Input>

export const Default: Story = {
  render: () => <Input placeholder="Enter text..." />,
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => <Input placeholder="Disabled input" disabled />,
}

export const Error: Story = {
  render: () => <Input placeholder="Invalid input" aria-invalid />,
}

export const Empty: Story = {
  render: () => <Input placeholder="Empty input" />,
}

export const WithLongText: Story = {
  render: () => (
    <Input
      placeholder="This is a very long placeholder text that should demonstrate how the input component handles extended content without breaking the layout"
    />
  ),
}