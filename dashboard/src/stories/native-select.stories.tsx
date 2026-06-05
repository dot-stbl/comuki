import type { Meta, StoryObj } from "@storybook/react"

import { NativeSelect } from "@/components/ui/native-select"

const meta: Meta<typeof NativeSelect> = {
  title: "UI/NativeSelect",
  component: NativeSelect,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof NativeSelect>

export const Default: Story = {
  render: () => (
    <NativeSelect>
      <option>Option one</option>
      <option>Option two</option>
      <option>Option three</option>
    </NativeSelect>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <NativeSelect disabled>
      <option>Disabled option</option>
    </NativeSelect>
  ),
}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <NativeSelect>
      <option>Only option</option>
    </NativeSelect>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <NativeSelect>
      <option>Short option</option>
      <option>
        This Is A Very Long Select Option Text That Should Demonstrate Proper Text Handling
      </option>
    </NativeSelect>
  ),
}