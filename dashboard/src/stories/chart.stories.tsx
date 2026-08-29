import type { Meta, StoryObj } from "@storybook/react"

import { ChartContainer } from "@/shared/ui/chart"

const meta: Meta<typeof ChartContainer> = {
  title: "UI/Chart",
  component: ChartContainer,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ChartContainer>

export const Default: Story = {
  render: () => (
    <ChartContainer className="h-48 w-80" config={{}}>
      <div className="flex h-full items-center justify-center rounded-md border border-dashed bg-muted text-xs text-muted-foreground">
        Chart content
      </div>
    </ChartContainer>
  ),
}

export const Loading: Story = {
  render: () => (
    <ChartContainer className="h-48 w-80" config={{}}>
      <div className="flex h-full animate-pulse items-center justify-center rounded-md bg-muted text-xs text-muted-foreground">
        Loading chart...
      </div>
    </ChartContainer>
  ),
}

export const Disabled: Story = {
  render: () => (
    <ChartContainer className="h-48 w-80 opacity-50" config={{}}>
      <div className="flex h-full items-center justify-center rounded-md border border-dashed text-xs text-muted-foreground">
        Chart disabled
      </div>
    </ChartContainer>
  ),
}

export const Error: Story = {
  render: () => (
    <ChartContainer className="h-48 w-80" config={{}}>
      <div className="flex h-full items-center justify-center rounded-md border border-destructive/30 bg-destructive/5 text-xs text-destructive">
        Chart failed to load
      </div>
    </ChartContainer>
  ),
}

export const Empty: Story = {
  render: () => (
    <ChartContainer className="h-48 w-80" config={{}}>
      <div className="flex h-full items-center justify-center rounded-md border border-dashed text-xs text-muted-foreground">
        No data available
      </div>
    </ChartContainer>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <ChartContainer className="h-48 w-80" config={{}}>
      <div className="flex h-full flex-col items-center justify-center gap-2 rounded-md border border-dashed bg-muted p-4 text-center text-xs text-muted-foreground">
        <span>Very Long Chart Title Label Text</span>
        <span>Subtitle with additional context information</span>
      </div>
    </ChartContainer>
  ),
}