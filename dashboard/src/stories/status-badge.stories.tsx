import type { Meta, StoryObj } from "@storybook/react"

import { StatusBadge } from "@/shared/ui/status-badge"

const meta: Meta<typeof StatusBadge> = {
  title: "Comuki/StatusBadge",
  component: StatusBadge,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  argTypes: {
    status: {
      control: "select",
      options: ["running", "success", "failed", "waiting", "queued", "escalated"],
    },
    size: {
      control: "radio",
      options: ["sm", "md"],
    },
  },
}

export default meta
type Story = StoryObj<typeof StatusBadge>

export const Running: Story = {
  args: { status: "running" },
}

export const Success: Story = {
  args: { status: "success" },
}

export const Failed: Story = {
  args: { status: "failed" },
}

export const Waiting: Story = {
  args: { status: "waiting" },
}

export const Queued: Story = {
  args: { status: "queued" },
}

export const Escalated: Story = {
  args: { status: "escalated" },
}

export const WithLongText: Story = {
  args: {
    status: "running",
    children: "Long Running Task With Extra Detail",
  },
}

export const Small: Story = {
  args: { status: "running", size: "sm" },
}