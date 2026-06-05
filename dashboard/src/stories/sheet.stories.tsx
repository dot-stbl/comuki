import type { Meta, StoryObj } from "@storybook/react"

import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet"
import { Button } from "@/components/ui/button"

const meta: Meta<typeof Sheet> = {
  title: "UI/Sheet",
  component: Sheet,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Sheet>

export const Default: Story = {
  render: () => (
    <Sheet open>
      <SheetTrigger asChild>
        <Button>Open sheet</Button>
      </SheetTrigger>
      <SheetContent>
        <SheetHeader>
          <SheetTitle>Sheet title</SheetTitle>
          <SheetDescription>Sheet description goes here.</SheetDescription>
        </SheetHeader>
        <p className="text-xs text-muted-foreground mt-4">Sheet content body.</p>
      </SheetContent>
    </Sheet>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Sheet open>
      <SheetContent>
        <SheetHeader>
          <SheetTitle>
            This Is A Very Long Sheet Title Label That Should Demonstrate Proper Text Handling
          </SheetTitle>
          <SheetDescription>
            This is a very long sheet description that demonstrates how the component handles
            extended content without breaking the layout.
          </SheetDescription>
        </SheetHeader>
      </SheetContent>
    </Sheet>
  ),
}