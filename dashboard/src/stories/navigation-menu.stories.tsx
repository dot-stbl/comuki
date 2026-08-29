import type { Meta, StoryObj } from "@storybook/react"

import {
  NavigationMenu,
  NavigationMenuContent,
  NavigationMenuItem,
  NavigationMenuLink,
  NavigationMenuList,
  NavigationMenuTrigger,
} from "@/shared/ui/navigation-menu"

const meta: Meta<typeof NavigationMenu> = {
  title: "UI/NavigationMenu",
  component: NavigationMenu,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof NavigationMenu>

export const Default: Story = {
  render: () => (
    <NavigationMenu>
      <NavigationMenuList>
        <NavigationMenuItem>
          <NavigationMenuTrigger>Getting started</NavigationMenuTrigger>
          <NavigationMenuContent>
            <p className="text-xs text-muted-foreground p-2">Navigation content</p>
          </NavigationMenuContent>
        </NavigationMenuItem>
        <NavigationMenuItem>
          <NavigationMenuLink href="#" asChild>
            <NavigationMenuLink>Documentation</NavigationMenuLink>
          </NavigationMenuLink>
        </NavigationMenuItem>
      </NavigationMenuList>
    </NavigationMenu>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <NavigationMenu>
      <NavigationMenuList>
        <NavigationMenuItem>
          <NavigationMenuTrigger>
            This Is A Very Long Navigation Trigger Label
          </NavigationMenuTrigger>
          <NavigationMenuContent>
            <p className="text-xs text-muted-foreground p-2">
              Content for the long trigger
            </p>
          </NavigationMenuContent>
        </NavigationMenuItem>
      </NavigationMenuList>
    </NavigationMenu>
  ),
}