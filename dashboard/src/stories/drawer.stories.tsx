import type { Meta, StoryObj } from "@storybook/react"

import { Drawer, DrawerContent, DrawerDescription, DrawerHeader, DrawerTitle, DrawerTrigger } from "@/components/ui/drawer"
import { Button } from "@/components/ui/button"

const meta: Meta<typeof Drawer> = {
  title: "UI/Drawer",
  component: Drawer,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Drawer>

export const Default: Story = {
  render: () => (
    <Drawer open>
      <DrawerTrigger asChild>
        <Button>Open drawer</Button>
      </DrawerTrigger>
      <DrawerContent>
        <DrawerHeader>
          <DrawerTitle>Drawer title</DrawerTitle>
          <DrawerDescription>Drawer description goes here.</DrawerDescription>
        </DrawerHeader>
        <div className="px-4 pb-4">
          <p className="text-xs text-muted-foreground">Drawer content body.</p>
        </div>
      </DrawerContent>
    </Drawer>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Drawer open>
      <DrawerContent>
        <DrawerHeader>
          <DrawerTitle>
            This Is A Very Long Drawer Title Label That Should Demonstrate Proper Text Handling
          </DrawerTitle>
          <DrawerDescription>
            This is a very long drawer description that demonstrates how the component handles
            extended content without breaking the layout.
          </DrawerDescription>
        </DrawerHeader>
      </DrawerContent>
    </Drawer>
  ),
}