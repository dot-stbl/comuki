import type { Meta, StoryObj } from "@storybook/react"

import { Field, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"

const meta: Meta<typeof Field> = {
  title: "UI/Field",
  component: Field,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Field>

export const Default: Story = {
  render: () => (
    <Field>
      <FieldLabel>Email</FieldLabel>
      <Input placeholder="you@example.com" />
    </Field>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <Field>
      <FieldLabel>Email</FieldLabel>
      <Input placeholder="you@example.com" disabled />
    </Field>
  ),
}

export const Error: Story = {
  render: () => (
    <Field>
      <FieldLabel>Email</FieldLabel>
      <Input placeholder="you@example.com" aria-invalid />
    </Field>
  ),
}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Field>
      <FieldLabel>
        This Is A Very Long Field Label Text That Should Demonstrate Proper Text Handling In The Component
      </FieldLabel>
      <Input placeholder="Long label field placeholder text" />
    </Field>
  ),
}