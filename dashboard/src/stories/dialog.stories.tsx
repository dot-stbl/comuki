import type { Meta, StoryObj } from "@storybook/react"

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogFooter,
  DialogClose,
  DialogOverlay,
} from "@/shared/ui/dialog"
import { Button } from "@/shared/ui/button"

const meta: Meta<typeof Dialog> = {
  title: "UI/Dialog",
  component: Dialog,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Dialog>

export const Default: Story = {
  render: () => (
    <Dialog open>
      <DialogTrigger asChild>
        <Button>Open dialog</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Dialog title</DialogTitle>
          <DialogDescription>Dialog description goes here.</DialogDescription>
        </DialogHeader>
        <p className="text-xs text-muted-foreground">Dialog content body.</p>
        <DialogFooter showCloseButton>
          <DialogClose asChild>
            <Button variant="outline">Cancel</Button>
          </DialogClose>
          <Button>Confirm</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  ),
}

export const Loading: Story = {
  render: () => (
    <Dialog open>
      <DialogOverlay />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Loading...</DialogTitle>
        </DialogHeader>
        <p className="text-xs text-muted-foreground">Please wait.</p>
      </DialogContent>
    </Dialog>
  ),
}

export const Disabled: Story = {
  render: () => (
    <Dialog open>
      <DialogOverlay />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Dialog</DialogTitle>
          <DialogDescription>All controls are disabled.</DialogDescription>
        </DialogHeader>
        <DialogFooter showCloseButton>
          <Button variant="outline" disabled>Cancel</Button>
          <Button disabled>Confirm</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  ),
}

export const Error: Story = {
  render: () => (
    <Dialog open>
      <DialogOverlay />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Error occurred</DialogTitle>
          <DialogDescription>Something went wrong.</DialogDescription>
        </DialogHeader>
        <p className="text-xs text-destructive">Could not complete the action.</p>
      </DialogContent>
    </Dialog>
  ),
}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Dialog open>
      <DialogOverlay />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            This Is A Very Long Dialog Title Label That Should Demonstrate Proper Text Handling
          </DialogTitle>
          <DialogDescription>
            This is a very long dialog description that demonstrates how the component handles
            extended content without breaking the layout. It should wrap properly and maintain
            readability across all viewport sizes.
          </DialogDescription>
        </DialogHeader>
        <p className="text-xs text-muted-foreground">
          This is a very long content text that demonstrates the dialog component handling
          extensive content without any layout issues.
        </p>
      </DialogContent>
    </Dialog>
  ),
}