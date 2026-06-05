import type { Meta, StoryObj } from "@storybook/react"

import { Item, ItemGroup, ItemTitle } from "@/components/ui/item"

const meta: Meta<typeof Item> = {
  title: "UI/Item",
  component: Item,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Item>

export const Default: Story = {
  render: () => (
    <ItemGroup>
      <Item>
        <ItemTitle>Item title</ItemTitle>
      </Item>
      <Item>
        <ItemTitle>Another item</ItemTitle>
      </Item>
    </ItemGroup>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {
  render: () => <ItemGroup />,
}

export const WithLongText: Story = {
  render: () => (
    <ItemGroup>
      <Item>
        <ItemTitle>
          This Is A Very Long Item Label Text That Should Demonstrate Proper Text Handling In The Component
        </ItemTitle>
      </Item>
    </ItemGroup>
  ),
}