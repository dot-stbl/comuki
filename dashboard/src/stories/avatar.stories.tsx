import type { Meta, StoryObj } from "@storybook/react"

import {
  Avatar,
  AvatarFallback,
  AvatarImage,
} from "@/components/ui/avatar"

const meta: Meta<typeof Avatar> = {
  title: "UI/Avatar",
  component: Avatar,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Avatar>

export const Default: Story = {
  render: () => (
    <Avatar>
      <AvatarImage src="https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?w=80&q=80" alt="User avatar" />
      <AvatarFallback>JD</AvatarFallback>
    </Avatar>
  ),
}

export const Loading: Story = {
  render: () => (
    <Avatar>
      <div className="size-full animate-pulse rounded-full bg-muted" />
    </Avatar>
  ),
}

export const Disabled: Story = {
  render: () => (
    <Avatar>
      <AvatarFallback className="opacity-50">JD</AvatarFallback>
    </Avatar>
  ),
}

export const Error: Story = {
  render: () => (
    <Avatar>
      <AvatarImage src="/nonexistent.jpg" alt="Broken avatar" />
      <AvatarFallback className="text-destructive">!</AvatarFallback>
    </Avatar>
  ),
}

export const Empty: Story = {
  render: () => (
    <Avatar>
      <AvatarFallback>--</AvatarFallback>
    </Avatar>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <Avatar>
      <AvatarFallback>JDC</AvatarFallback>
    </Avatar>
  ),
}