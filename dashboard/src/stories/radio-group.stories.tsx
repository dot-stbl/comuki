import type { Meta, StoryObj } from "@storybook/react"

import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Label } from "@/components/ui/label"

const meta: Meta<typeof RadioGroup> = {
  title: "UI/RadioGroup",
  component: RadioGroup,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RadioGroup>

export const Default: Story = {
  render: () => (
    <RadioGroup defaultValue="one">
      <div className="flex items-center gap-2">
        <RadioGroupItem value="one" id="r1" />
        <Label htmlFor="r1">Option one</Label>
      </div>
      <div className="flex items-center gap-2">
        <RadioGroupItem value="two" id="r2" />
        <Label htmlFor="r2">Option two</Label>
      </div>
    </RadioGroup>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <RadioGroup defaultValue="one">
      <div className="flex items-center gap-2">
        <RadioGroupItem value="one" id="r1" disabled />
        <Label htmlFor="r1" className="opacity-50">Disabled option</Label>
      </div>
    </RadioGroup>
  ),
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <RadioGroup defaultValue="one">
      <div className="flex items-center gap-2">
        <RadioGroupItem value="one" id="r1" />
        <Label htmlFor="r1">
          This is a very long radio group label text that demonstrates how the component
          handles extended content without breaking the layout
        </Label>
      </div>
    </RadioGroup>
  ),
}