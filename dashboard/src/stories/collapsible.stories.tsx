import * as React from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible"
import { Button } from "@/components/ui/button"

const meta: Meta<typeof Collapsible> = {
  title: "UI/Collapsible",
  component: Collapsible,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Collapsible>

export const Default: Story = {
  render: () => {
    const [open, setOpen] = React.useState(false)
    return (
      <Collapsible open={open} onOpenChange={setOpen}>
        <CollapsibleTrigger asChild>
          <Button variant="ghost" size="sm">Toggle section</Button>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <div className="mt-2 rounded-md border border-dashed p-4 text-xs text-muted-foreground">
            Collapsed content goes here
          </div>
        </CollapsibleContent>
      </Collapsible>
    )
  },
}

export const Loading: Story = {
  render: () => {
    const [open] = React.useState(false)
    return (
      <Collapsible open={open}>
        <CollapsibleContent>
          <div className="mt-2 h-16 w-48 animate-pulse rounded-md bg-muted" />
        </CollapsibleContent>
      </Collapsible>
    )
  },
}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <Collapsible>
      <CollapsibleContent>
        <div className="mt-2 text-xs text-muted-foreground">No content</div>
      </CollapsibleContent>
    </Collapsible>
  ),
}

export const WithLongText: Story = {
  render: () => {
    const [open, setOpen] = React.useState(false)
    return (
      <Collapsible open={open} onOpenChange={setOpen}>
        <CollapsibleTrigger asChild>
          <Button variant="ghost" size="sm">Click to expand section with very long title text</Button>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <div className="mt-2 rounded-md border border-dashed p-4 text-xs text-muted-foreground">
            This is the collapsed content that appears when the user clicks the
            trigger. It contains a very long text to demonstrate how the
            component handles extended content without breaking the layout or
            causing any overflow issues in the collapsed state.
          </div>
        </CollapsibleContent>
      </Collapsible>
    )
  },
}
