import type { Meta, StoryObj } from "@storybook/react"

import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList, CommandSeparator } from "@/components/ui/command"

const meta: Meta<typeof Command> = {
  title: "UI/Command",
  component: Command,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Command>

export const Default: Story = {
  render: () => (
    <Command className="w-64">
      <CommandInput placeholder="Type a command..." />
      <CommandList>
        <CommandEmpty>No results found.</CommandEmpty>
        <CommandGroup heading="Suggestions">
          <CommandItem>Calendar</CommandItem>
          <CommandItem>Search</CommandItem>
          <CommandItem>Settings</CommandItem>
        </CommandGroup>
        <CommandSeparator />
        <CommandGroup heading="Actions">
          <CommandItem>New file</CommandItem>
          <CommandItem>Delete</CommandItem>
        </CommandGroup>
      </CommandList>
    </Command>
  ),
}

export const Loading: Story = {
  render: () => (
    <Command className="w-64">
      <CommandInput disabled placeholder="Loading..." />
    </Command>
  ),
}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {
  render: () => (
    <Command className="w-64">
      <CommandInput placeholder="Search..." />
      <CommandList>
        <CommandEmpty>No results found.</CommandEmpty>
      </CommandList>
    </Command>
  ),
}

export const WithLongText: Story = {
  render: () => (
    <Command className="w-64">
      <CommandInput placeholder="Search commands with very long placeholder text..." />
      <CommandList>
        <CommandGroup heading="Very Long Category Name For The Command Group">
          <CommandItem>
            This Is A Very Long Command Label That Should Demonstrate Proper Text Handling
          </CommandItem>
          <CommandItem>Short command</CommandItem>
        </CommandGroup>
      </CommandList>
    </Command>
  ),
}