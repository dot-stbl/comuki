import type { Meta, StoryObj } from "@storybook/react"

import { HoverCard, HoverCardContent, HoverCardTrigger } from "@/components/ui/hover-card"
import { Button } from "@/components/ui/button"

const meta: Meta<typeof HoverCard> = {
  title: "UI/HoverCard",
  component: HoverCard,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof HoverCard>

export const Default: Story = {
  render: () => (
    <HoverCard>
      <HoverCardTrigger asChild>
        <Button variant="link" size="sm">Hover over me</Button>
      </HoverCardTrigger>
      <HoverCardContent>
        <p className="text-xs text-muted-foreground">
          Hover card content appears here.
        </p>
      </HoverCardContent>
    </HoverCard>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <HoverCard>
      <HoverCardTrigger asChild>
        <Button variant="link" size="sm">Hover over me</Button>
      </HoverCardTrigger>
      <HoverCardContent>
        <p className="text-xs text-muted-foreground">
          This is a very long hover card content that demonstrates how the component handles
          extended text without breaking the layout or causing any overflow issues.
        </p>
      </HoverCardContent>
    </HoverCard>
  ),
}