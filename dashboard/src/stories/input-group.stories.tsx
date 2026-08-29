import type { Meta, StoryObj } from "@storybook/react"

import { InputGroup } from "@/shared/ui/input-group"
import { Input } from "@/shared/ui/input"

const meta: Meta<typeof InputGroup> = {
  title: "UI/InputGroup",
  component: InputGroup,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof InputGroup>

export const Default: Story = {
  render: () => (
    <InputGroup>
      <Input placeholder="Search..." />
    </InputGroup>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <InputGroup>
      <Input placeholder="Search..." disabled />
    </InputGroup>
  ),
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <InputGroup>
      <Input placeholder="This is a very long placeholder text that should demonstrate proper overflow handling" />
    </InputGroup>
  ),
}