import type { Meta, StoryObj } from "@storybook/react"

import { Spinner } from "@/shared/ui/spinner"

const meta: Meta<typeof Spinner> = {
  title: "UI/Spinner",
  component: Spinner,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Spinner>

export const Default: Story = {
  args: {},
}

export const Loading: Story = {
  args: {},
}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {}