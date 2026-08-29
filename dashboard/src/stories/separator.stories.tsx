import type { Meta, StoryObj } from "@storybook/react"

import { Separator } from "@/shared/ui/separator"

const meta: Meta<typeof Separator> = {
  title: "UI/Separator",
  component: Separator,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Separator>

export const Default: Story = {
  render: () => (
    <div className="w-64 space-y-4">
      <p className="text-xs text-muted-foreground">Above separator</p>
      <Separator />
      <p className="text-xs text-muted-foreground">Below separator</p>
    </div>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {}