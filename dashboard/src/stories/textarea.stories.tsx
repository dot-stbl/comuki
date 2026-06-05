import type { Meta, StoryObj } from "@storybook/react"

import { Textarea } from "@/components/ui/textarea"

const meta: Meta<typeof Textarea> = {
  title: "UI/Textarea",
  component: Textarea,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Textarea>

export const Default: Story = {
  render: () => <Textarea placeholder="Enter text..." />,
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => <Textarea placeholder="Disabled" disabled />,
}

export const Error: Story = {
  render: () => <Textarea placeholder="Invalid" aria-invalid />,
}

export const Empty: Story = {
  render: () => <Textarea placeholder="Empty textarea" />,
}

export const WithLongText: Story = {
  render: () => (
    <Textarea
      placeholder="This is a very long placeholder text that demonstrates how the textarea component handles extended content. The user can type a very long message that wraps properly and maintains readability without causing any layout issues."
    />
  ),
}