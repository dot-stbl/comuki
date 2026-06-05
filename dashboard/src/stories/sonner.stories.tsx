import type { Meta, StoryObj } from "@storybook/react"

import { Toaster } from "@/components/ui/sonner"

const meta: Meta<typeof Toaster> = {
  title: "UI/Sonner",
  component: Toaster,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Toaster>

export const Default: Story = {
  render: () => <Toaster />,
}

export const Loading: Story = {}
export const Disabled: Story = {}
export const Error: Story = {}
export const Empty: Story = {}
export const WithLongText: Story = {}