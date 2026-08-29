import type { Meta, StoryObj } from "@storybook/react"

import { Alert } from "@/shared/ui/alert"

const meta: Meta<typeof Alert> = {
  title: "UI/Alert",
  component: Alert,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Alert>

export const Default: Story = {
  args: { children: "An alert message for the user." },
}

export const Loading: Story = {
  args: { children: "Loading your data, please wait..." },
}

export const Disabled: Story = {}

export const Error: Story = {
  args: {
    variant: "destructive",
    children: "Something went wrong. Please try again.",
  },
}

export const Empty: Story = {}

export const WithLongText: Story = {
  args: {
    children:
      "This is a very long alert message that contains a lot of text to test how the component handles overflow content and ensures that the text wraps correctly without causing any layout issues or horizontal scrolling.",
  },
}