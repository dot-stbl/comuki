import type { Meta, StoryObj } from "@storybook/react"

import { Button } from "@/components/ui/button"
import { ButtonGroup } from "@/components/ui/button-group"

const meta: Meta<typeof ButtonGroup> = {
  title: "UI/ButtonGroup",
  component: ButtonGroup,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ButtonGroup>

export const Default: Story = {
  render: () => (
    <ButtonGroup>
      <Button>One</Button>
      <Button>Two</Button>
      <Button>Three</Button>
    </ButtonGroup>
  ),
}

export const Loading: Story = {
  render: () => (
    <ButtonGroup>
      <Button disabled>Loading</Button>
      <Button disabled>...</Button>
    </ButtonGroup>
  ),
}

export const Disabled: Story = {
  render: () => (
    <ButtonGroup>
      <Button disabled>Disabled</Button>
      <Button disabled>Option</Button>
    </ButtonGroup>
  ),
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <ButtonGroup>
      <Button>
        This Is A Very Long Button Label
      </Button>
      <Button>Short</Button>
    </ButtonGroup>
  ),
}