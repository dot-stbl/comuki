import type { Meta, StoryObj } from "@storybook/react"

import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Button } from "@/components/ui/button"

const meta: Meta<typeof Popover> = {
  title: "UI/Popover",
  component: Popover,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Popover>

export const Default: Story = {
  render: () => (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm">Open popover</Button>
      </PopoverTrigger>
      <PopoverContent>
        <p className="text-xs text-muted-foreground">Popover content.</p>
      </PopoverContent>
    </Popover>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm">Open</Button>
      </PopoverTrigger>
      <PopoverContent>
        <p className="text-xs text-muted-foreground">
          This is a very long popover content text that demonstrates how the component handles
          extended text without breaking the layout or causing any overflow issues.
        </p>
      </PopoverContent>
    </Popover>
  ),
}