import type { Meta, StoryObj } from "@storybook/react"
import { fn } from "@storybook/test"

import { AlertDialog } from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"

const meta: Meta<typeof AlertDialog.Root> = {
  title: "UI/AlertDialog",
  component: AlertDialog.Root,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof AlertDialog.Root>

export const Default: Story = {
  render: () => (
    <AlertDialog.Root open>
      <AlertDialog.Trigger asChild>
        <Button>Open dialog</Button>
      </AlertDialog.Trigger>
      <AlertDialog.Portal>
        <AlertDialog.Overlay />
        <AlertDialog.Content>
          <AlertDialog.Header>
            <AlertDialog.Title>Are you absolutely sure?</AlertDialog.Title>
            <AlertDialog.Description>
              This action cannot be undone. This will permanently delete your
              account and remove your data from our servers.
            </AlertDialog.Description>
          </AlertDialog.Header>
          <AlertDialog.Footer showCloseButton>
            <AlertDialog.Cancel asChild>
              <Button variant="outline">Cancel</Button>
            </AlertDialog.Cancel>
            <AlertDialog.Action asChild>
              <Button variant="destructive">Continue</Button>
            </AlertDialog.Action>
          </AlertDialog.Footer>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  ),
}

export const Loading: Story = {
  render: () => (
    <AlertDialog.Root open>
      <AlertDialog.Portal>
        <AlertDialog.Overlay />
        <AlertDialog.Content>
          <AlertDialog.Header>
            <AlertDialog.Title>Deleting account...</AlertDialog.Title>
            <AlertDialog.Description>Please wait</AlertDialog.Description>
          </AlertDialog.Header>
          <AlertDialog.Footer showCloseButton>
            <Button disabled>Cancel</Button>
          </AlertDialog.Footer>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  ),
}

export const Disabled: Story = {
  render: () => (
    <AlertDialog.Root open>
      <AlertDialog.Portal>
        <AlertDialog.Overlay />
        <AlertDialog.Content>
          <AlertDialog.Header>
            <AlertDialog.Title>Confirm action</AlertDialog.Title>
            <AlertDialog.Description>
              Are you sure you want to proceed?
            </AlertDialog.Description>
          </AlertDialog.Header>
          <AlertDialog.Footer showCloseButton>
            <Button variant="outline" disabled>Cancel</Button>
            <Button variant="destructive" disabled>Delete</Button>
          </AlertDialog.Footer>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  ),
}

export const Error: Story = {
  render: () => (
    <AlertDialog.Root open>
      <AlertDialog.Portal>
        <AlertDialog.Overlay />
        <AlertDialog.Content>
          <AlertDialog.Header>
            <AlertDialog.Title>Delete failed</AlertDialog.Title>
            <AlertDialog.Description>
              Could not delete the account. Please try again.
            </AlertDialog.Description>
          </AlertDialog.Header>
          <AlertDialog.Footer showCloseButton>
            <Button>Retry</Button>
          </AlertDialog.Footer>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  ),
}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <AlertDialog.Root open>
      <AlertDialog.Portal>
        <AlertDialog.Overlay />
        <AlertDialog.Content>
          <AlertDialog.Header>
            <AlertDialog.Title>Terms of Service Agreement</AlertDialog.Title>
            <AlertDialog.Description>
              By using this service you agree to be bound by the following
              terms and conditions. This agreement constitutes a legally binding
              contract between you and our company. Please read carefully
              before proceeding. You acknowledge that you have read, understood,
              and agree to be bound by these terms. If you do not agree to these
              terms, you must not use this service. We reserve the right to
              modify these terms at any time without prior notice.
            </AlertDialog.Description>
          </AlertDialog.Header>
          <AlertDialog.Footer showCloseButton>
            <AlertDialog.Cancel asChild>
              <Button variant="outline">Decline</Button>
            </AlertDialog.Cancel>
            <AlertDialog.Action asChild>
              <Button>Accept</Button>
            </AlertDialog.Action>
          </AlertDialog.Footer>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  ),
}