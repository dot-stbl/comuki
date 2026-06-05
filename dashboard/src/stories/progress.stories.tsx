import type { Meta, StoryObj } from "@storybook/react"

import { Progress } from "@/components/ui/progress"

const meta: Meta<typeof Progress> = {
  title: "UI/Progress",
  component: Progress,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Progress>

export const Default: Story = {
  args: { value: 65 },
}

export const Loading: Story = {
  args: { value: undefined },
}

export const Disabled: Story = {
  args: { value: 50 },
}

export const Error: Story = {}

export const Empty: Story = {
  args: { value: 0 },
}

export const WithLongText: Story = {}