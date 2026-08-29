import type { Meta, StoryObj } from "@storybook/react"

import { Toggle } from "@/shared/ui/toggle"

const meta: Meta<typeof Toggle> = {
  title: "UI/Toggle",
  component: Toggle,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Toggle>

export const Default: Story = {
  render: () => <Toggle>Toggle</Toggle>,
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => <Toggle disabled>Disabled</Toggle>,
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Toggle>This Is A Very Long Toggle Label Text</Toggle>
  ),
}