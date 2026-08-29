import type { Meta, StoryObj } from "@storybook/react"

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/shared/ui/alert-dialog"
import { Button } from "@/shared/ui/button"

const meta: Meta<typeof AlertDialog> = {
  title: "UI/AlertDialog",
  component: AlertDialog,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof AlertDialog>

export const Default: Story = {
  render: () => (
    <AlertDialog open>
      <AlertDialogTrigger asChild>
        <Button>Open dialog</Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Are you absolutely sure?</AlertDialogTitle>
          <AlertDialogDescription>
            This action cannot be undone. This will permanently delete your
            account and remove your data from our servers.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel asChild>
            <Button variant="outline">Cancel</Button>
          </AlertDialogCancel>
          <AlertDialogAction asChild>
            <Button variant="destructive">Continue</Button>
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  ),
}

export const Loading: Story = {
  render: () => (
    <AlertDialog open>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deleting account...</AlertDialogTitle>
          <AlertDialogDescription>Please wait</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <Button disabled>Cancel</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  ),
}

export const Disabled: Story = {
  render: () => (
    <AlertDialog open>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Confirm action</AlertDialogTitle>
          <AlertDialogDescription>
            Are you sure you want to proceed?
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <Button variant="outline" disabled>Cancel</Button>
          <Button variant="destructive" disabled>Delete</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  ),
}

export const Error: Story = {
  render: () => (
    <AlertDialog open>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete failed</AlertDialogTitle>
          <AlertDialogDescription>
            Could not delete the account. Please try again.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <Button>Retry</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  ),
}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <AlertDialog open>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Terms of Service Agreement</AlertDialogTitle>
          <AlertDialogDescription>
            By using this service you agree to be bound by the following
            terms and conditions. This agreement constitutes a legally binding
            contract between you and our company. Please read carefully
            before proceeding. You acknowledge that you have read, understood,
            and agree to be bound by these terms. If you do not agree to these
            terms, you must not use this service. We reserve the right to
            modify these terms at any time without prior notice.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel asChild>
            <Button variant="outline">Decline</Button>
          </AlertDialogCancel>
          <AlertDialogAction asChild>
            <Button>Accept</Button>
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  ),
}