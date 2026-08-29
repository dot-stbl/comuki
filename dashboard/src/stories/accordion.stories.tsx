import type { Meta, StoryObj } from "@storybook/react"

import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from "@/shared/ui/accordion"

const meta: Meta<typeof Accordion> = {
  title: "UI/Accordion",
  component: Accordion,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Accordion>

export const Default: Story = {
  render: () => (
    <div className="w-80">
      <AccordionItem value="a">
        <AccordionTrigger>Section one</AccordionTrigger>
        <AccordionContent>Content for section one.</AccordionContent>
      </AccordionItem>
      <AccordionItem value="b">
        <AccordionTrigger>Section two</AccordionTrigger>
        <AccordionContent>Content for section two.</AccordionContent>
      </AccordionItem>
    </div>
  ),
}

export const Loading: Story = {
  render: () => (
    <div className="w-80 space-y-2">
      {[1, 2].map((i) => (
        <div key={i} className="h-12 animate-pulse rounded-md bg-muted" />
      ))}
    </div>
  ),
}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {
  render: () => (
    <div className="w-80">
      <AccordionItem value="a">
        <AccordionTrigger>
          This Is A Very Long Accordion Trigger Label That Should Demonstrate Proper Text Handling
        </AccordionTrigger>
        <AccordionContent>
          This is a very long accordion content text that demonstrates how the component handles
          extended content without breaking the layout or causing any overflow issues in the
          collapsed or expanded state.
        </AccordionContent>
      </AccordionItem>
    </div>
  ),
}