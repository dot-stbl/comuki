import type { Meta, StoryObj } from "@storybook/react"

import { ModeToggle } from "@/components/ui/mode-toggle"

const meta: Meta<typeof ModeToggle> = {
  title: "Comuki/ModeToggle",
  component: ModeToggle,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ModeToggle>

export const Default: Story = {}

export const WithLongText: Story = {}