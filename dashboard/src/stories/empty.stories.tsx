import type { Meta, StoryObj } from "@storybook/react"

import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@/shared/ui/empty"

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
    <Empty>
      <EmptyHeader>
        <EmptyTitle>No data</EmptyTitle>
        <EmptyDescription>There is nothing to display here yet.</EmptyDescription>
      </EmptyHeader>
    </Empty>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {
  render: () => (
    <Empty>
      <EmptyHeader>
        <EmptyTitle>Failed to load</EmptyTitle>
        <EmptyDescription>Something went wrong while fetching data.</EmptyDescription>
      </EmptyHeader>
    </Empty>
  ),
}

export const Empty_Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Empty>
      <EmptyHeader>
        <EmptyTitle>No Results Found</EmptyTitle>
        <EmptyDescription>
          We could not find any matching results for your search criteria. Please try
          adjusting your filters or search terms to find what you are looking for.
        </EmptyDescription>
      </EmptyHeader>
    </Empty>
  ),
}