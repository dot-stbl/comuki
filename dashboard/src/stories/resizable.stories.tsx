import type { Meta, StoryObj } from "@storybook/react"

import { ResizablePanel, ResizablePanelGroup } from "@/components/ui/resizable"

const meta: Meta<typeof ResizablePanelGroup> = {
  title: "UI/Resizable",
  component: ResizablePanelGroup,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ResizablePanelGroup>

export const Default: Story = {
  render: () => (
    <ResizablePanelGroup className="h-48 w-96">
      <ResizablePanel defaultSize={50}>
        <div className="flex size-full items-center justify-center rounded-md border border-dashed bg-muted text-xs text-muted-foreground">
          Panel A
        </div>
      </ResizablePanel>
      <ResizablePanel defaultSize={50}>
        <div className="flex size-full items-center justify-center rounded-md border border-dashed bg-muted text-xs text-muted-foreground">
          Panel B
        </div>
      </ResizablePanel>
    </ResizablePanelGroup>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <ResizablePanelGroup className="h-48 w-96">
      <ResizablePanel defaultSize={33}>
        <div className="flex size-full items-center justify-center rounded-md border border-dashed bg-muted p-2 text-center text-xs text-muted-foreground">
          This Is A Very Long Panel A Label Text
        </div>
      </ResizablePanel>
      <ResizablePanel defaultSize={67}>
        <div className="flex size-full items-center justify-center rounded-md border border-dashed bg-muted p-2 text-center text-xs text-muted-foreground">
          Panel B
        </div>
      </ResizablePanel>
    </ResizablePanelGroup>
  ),
}