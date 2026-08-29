import type { Meta, StoryObj } from "@storybook/react"

import { StatusBadge } from "./status-badge"

const meta = {
  title: "UI Kit/Feedback/StatusBadge",
  component: StatusBadge,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  argTypes: {
    status: {
      control: "select",
      options: [
        "running",
        "success",
        "failed",
        "waiting",
        "queued",
        "escalated",
      ],
    },
    size: {
      control: "radio",
      options: ["sm", "md"],
    },
  },
} satisfies Meta<typeof StatusBadge>

export default meta
type Story = StoryObj<typeof meta>

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

export const Small: Story = {
  args: { status: "running", size: "sm" },
}

export const CustomLabel: Story = {
  args: {
    status: "running",
    children: "Claimed · worker-3",
  },
}
