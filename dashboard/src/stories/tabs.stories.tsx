import type { Meta, StoryObj } from "@storybook/react"

import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"

const meta: Meta<typeof Tabs> = {
  title: "UI/Tabs",
  component: Tabs,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Tabs>

export const Default: Story = {
  render: () => (
    <Tabs defaultValue="tab1">
      <TabsList>
        <TabsTrigger value="tab1">Tab one</TabsTrigger>
        <TabsTrigger value="tab2">Tab two</TabsTrigger>
        <TabsTrigger value="tab3">Tab three</TabsTrigger>
      </TabsList>
      <TabsContent value="tab1">
        <p className="text-xs text-muted-foreground">Content for tab one.</p>
      </TabsContent>
      <TabsContent value="tab2">
        <p className="text-xs text-muted-foreground">Content for tab two.</p>
      </TabsContent>
      <TabsContent value="tab3">
        <p className="text-xs text-muted-foreground">Content for tab three.</p>
      </TabsContent>
    </Tabs>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {
  render: () => (
    <Tabs defaultValue="tab1">
      <TabsList>
        <TabsTrigger value="tab1" disabled>Tab one</TabsTrigger>
        <TabsTrigger value="tab2" disabled>Tab two</TabsTrigger>
      </TabsList>
    </Tabs>
  ),
}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <Tabs defaultValue="tab1">
      <TabsList>
        <TabsTrigger value="tab1">
          This Is A Very Long Tab Label
        </TabsTrigger>
        <TabsTrigger value="tab2">Short</TabsTrigger>
      </TabsList>
      <TabsContent value="tab1">
        <p className="text-xs text-muted-foreground">
          This is a very long tab content text that demonstrates how the component
          handles extended content without breaking the layout.
        </p>
      </TabsContent>
    </Tabs>
  ),
}