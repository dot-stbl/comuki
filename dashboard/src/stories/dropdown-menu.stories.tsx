import type { Meta, StoryObj } from "@storybook/react"

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Button } from "@/components/ui/button"

const meta: Meta<typeof DropdownMenu> = {
  title: "UI/DropdownMenu",
  component: DropdownMenu,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof DropdownMenu>

export const Default: Story = {
  render: () => (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm">Open menu</Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent>
        <DropdownMenuLabel>Actions</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem>Copy</DropdownMenuItem>
        <DropdownMenuItem>Cut</DropdownMenuItem>
        <DropdownMenuItem>Paste</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <DropdownMenu>
      <DropdownMenuContent>
        <DropdownMenuItem className="opacity-50" disabled>Copy</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  ),
}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <DropdownMenu open>
      <DropdownMenuContent />
    </DropdownMenu>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm">Open</Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent>
        <DropdownMenuItem>
          This Is A Very Long Dropdown Menu Item Label That Should Demonstrate Proper Text Handling
        </DropdownMenuItem>
        <DropdownMenuItem>Short item</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  ),
}