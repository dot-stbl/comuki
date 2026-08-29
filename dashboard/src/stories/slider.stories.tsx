import type { Meta, StoryObj } from "@storybook/react"

import { Slider } from "@/shared/ui/slider"

const meta: Meta<typeof Slider> = {
  title: "UI/Slider",
  component: Slider,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Slider>

export const Default: Story = {
  render: () => (
    <div className="w-64">
      <Slider defaultValue={[50]} max={100} step={1} />
    </div>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <div className="w-64">
      <Slider defaultValue={[50]} max={100} disabled />
    </div>
  ),
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {}