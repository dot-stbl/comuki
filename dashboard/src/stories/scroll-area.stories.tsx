import type { Meta, StoryObj } from "@storybook/react"

import { ScrollArea } from "@/shared/ui/scroll-area"

const meta: Meta<typeof ScrollArea> = {
  title: "UI/ScrollArea",
  component: ScrollArea,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ScrollArea>

export const Default: Story = {
  render: () => (
    <ScrollArea className="h-32 w-64 rounded-md border p-4">
      <p className="text-xs text-muted-foreground">
        Scroll area content with enough text to require scrolling.
        This component wraps content and provides a custom scrollbar.
      </p>
    </ScrollArea>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <ScrollArea className="h-32 w-64 rounded-md border p-4">
      <p className="text-xs text-muted-foreground">
        This is a very long scroll area content text that demonstrates how the component
        handles extended text without breaking the layout. It should scroll properly and
        maintain readability across all viewport sizes. This content is intentionally long
        to demonstrate the scrolling behavior of the scroll area component in Storybook stories.
      </p>
    </ScrollArea>
  ),
}