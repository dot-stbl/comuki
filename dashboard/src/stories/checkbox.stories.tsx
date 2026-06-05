import type { Meta, StoryObj } from "@storybook/react"
import { fn } from "@storybook/test"

import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"

const meta: Meta<typeof Checkbox> = {
  title: "UI/Checkbox",
  component: Checkbox,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Checkbox>

export const Default: Story = {
  render: () => (
    <div className="flex items-center gap-2">
      <Checkbox id="terms" />
      <Label htmlFor="terms">Accept terms</Label>
    </div>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <div className="flex items-center gap-2">
      <Checkbox id="disabled" disabled />
      <Label htmlFor="disabled" className="opacity-50">Disabled option</Label>
    </div>
  ),
}

export const Error: Story = {
  render: () => (
    <div className="flex items-center gap-2">
      <Checkbox id="error" aria-invalid />
      <Label htmlFor="error" className="text-destructive">Invalid checkbox</Label>
    </div>
  ),
}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <div className="flex items-center gap-2">
      <Checkbox id="long" />
      <Label htmlFor="long">
        This is a very long label text that demonstrates how the checkbox
        component handles extended labels without breaking the layout or
        causing any overflow issues
      </Label>
    </div>
  ),
}