import type { Meta, StoryObj } from "@storybook/react"

import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"

const meta: Meta<typeof Select> = {
  title: "UI/Select",
  component: Select,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Select>

export const Default: Story = {
  render: () => (
    <Select>
      <SelectTrigger>
        <SelectValue placeholder="Select option" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="one">Option one</SelectItem>
        <SelectItem value="two">Option two</SelectItem>
        <SelectItem value="three">Option three</SelectItem>
      </SelectContent>
    </Select>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <Select>
      <SelectTrigger disabled>
        <SelectValue placeholder="Disabled" />
      </SelectTrigger>
    </Select>
  ),
}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <Select>
      <SelectTrigger>
        <SelectValue placeholder="No options" />
      </SelectTrigger>
      <SelectContent />
    </Select>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <Select>
      <SelectTrigger>
        <SelectValue placeholder="Select option" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="one">
          This Is A Very Long Select Option Text That Should Demonstrate Proper Text Handling
        </SelectItem>
        <SelectItem value="two">Short option</SelectItem>
      </SelectContent>
    </Select>
  ),
}