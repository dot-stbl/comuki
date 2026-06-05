import type { Meta, StoryObj } from "@storybook/react"

import { Switch } from "@/components/ui/switch"

const meta: Meta<typeof Switch> = {
  title: "UI/Switch",
  component: Switch,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Switch>

export const Default: Story = {
  render: () => <Switch />,
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => <Switch disabled />,
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {}