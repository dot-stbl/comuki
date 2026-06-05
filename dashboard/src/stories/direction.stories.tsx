import type { Meta, StoryObj } from "@storybook/react"

import { DirectionProvider } from "@/components/ui/direction"

const meta: Meta<typeof DirectionProvider> = {
  title: "UI/Direction",
  component: DirectionProvider,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof DirectionProvider>

export const Default: Story = {
  render: () => (
    <DirectionProvider dir="ltr">
      <p className="text-xs text-muted-foreground">Left-to-right direction active</p>
    </DirectionProvider>
  ),
}

export const Loading: Story = {}
export const Disabled: Story = {}
export const Error: Story = {}
export const Empty: Story = {}
export const WithLongText: Story = {}