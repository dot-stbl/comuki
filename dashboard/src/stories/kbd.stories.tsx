import type { Meta, StoryObj } from "@storybook/react"

import { Kbd } from "@/components/ui/kbd"

const meta: Meta<typeof Kbd> = {
  title: "UI/Kbd",
  component: Kbd,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Kbd>

export const Default: Story = {
  args: { children: "Cmd" },
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  args: { children: "Ctrl+Shift+Alt+Delete" },
}