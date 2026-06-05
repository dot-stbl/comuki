import type { Meta, StoryObj } from "@storybook/react"

import { Empty } from "@/components/ui/empty"

const meta: Meta<typeof Empty> = {
  title: "UI/Empty",
  component: Empty,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Empty>

export const Default: Story = {
  render: () => (
    <Empty
      label="No data"
      description="There is nothing to display here yet."
    />
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {
  render: () => (
    <Empty
      label="Failed to load"
      description="Something went wrong while fetching data."
    />
  ),
}

export const Empty_Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Empty
      label="No Results Found"
      description="We could not find any matching results for your search criteria. Please try adjusting your filters or search terms to find what you are looking for."
    />
  ),
}