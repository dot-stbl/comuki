import type { Meta, StoryObj } from "@storybook/react"

import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuItem,
  SidebarProvider,
} from "@/shared/ui/sidebar"

const meta: Meta<typeof Sidebar> = {
  title: "UI/Sidebar",
  component: Sidebar,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Sidebar>

export const Default: Story = {
  render: () => (
    <SidebarProvider>
      <Sidebar>
        <SidebarHeader>
          <p className="text-xs font-medium px-2 py-1">Navigation</p>
        </SidebarHeader>
        <SidebarContent>
          <SidebarGroup>
            <SidebarGroupLabel>Menu</SidebarGroupLabel>
            <SidebarMenu>
              <SidebarMenuItem>
                <p className="text-xs px-2 py-1">Dashboard</p>
              </SidebarMenuItem>
              <SidebarMenuItem>
                <p className="text-xs px-2 py-1">Runs</p>
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroup>
        </SidebarContent>
      </Sidebar>
    </SidebarProvider>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <SidebarProvider>
      <Sidebar>
        <SidebarContent>
          <SidebarGroup>
            <SidebarGroupLabel>
              This Is A Very Long Navigation Label
            </SidebarGroupLabel>
            <SidebarMenu>
              <SidebarMenuItem>
                <p className="text-xs px-2 py-1">
                  This Is A Very Long Navigation Item Label
                </p>
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroup>
        </SidebarContent>
      </Sidebar>
    </SidebarProvider>
  ),
}