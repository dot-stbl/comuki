import type { Meta, StoryObj } from "@storybook/react"

import { Combobox } from "@/shared/ui/combobox"

const meta: Meta<typeof Combobox> = {
  title: "UI/Combobox",
  component: Combobox,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Combobox>

export const Default: Story = {}
export const Loading: Story = {}
export const Disabled: Story = {}
export const Error: Story = {}
export const Empty: Story = {}
export const WithLongText: Story = {}