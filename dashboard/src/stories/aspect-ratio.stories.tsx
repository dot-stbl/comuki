import type { Meta, StoryObj } from "@storybook/react"

import { AspectRatio } from "@/components/ui/aspect-ratio"

const meta: Meta<typeof AspectRatio> = {
  title: "UI/AspectRatio",
  component: AspectRatio,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof AspectRatio>

export const Default: Story = {
  render: () => (
    <div className="w-64">
      <AspectRatio ratio={(16 / 9)}>
        <img
          src="https://images.unsplash.com/photo-1537953773345-d172ccf13cf4?w=800&q=80"
          alt="Landscape"
          className="size-full rounded-md object-cover"
        />
      </AspectRatio>
    </div>
  ),
}

export const Loading: Story = {
  render: () => (
    <div className="w-64">
      <AspectRatio ratio={16 / 9}>
        <div className="size-full animate-pulse rounded-md bg-muted" />
      </AspectRatio>
    </div>
  ),
}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <div className="w-64">
      <AspectRatio ratio={4 / 3}>
        <div className="flex size-full items-center justify-center rounded-md border border-dashed text-muted-foreground text-xs">
          No image
        </div>
      </AspectRatio>
    </div>
  ),
}

export const WithLongText: Story = {}