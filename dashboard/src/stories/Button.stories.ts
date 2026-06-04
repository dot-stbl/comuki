import type { Meta, StoryObj } from "@storybook/react"

import { Button } from "@/components/ui/button"

/**
 * Bootstrap story — proves the Storybook pipeline (Vite + theme + decorators).
 * Real Comuki stories (StatusBadge, RunCard, etc.) land in Phase 7
 * once the design tokens from comuki-dashboard-designspec.md are applied.
 */
const meta: Meta<typeof Button> = {
  title: "UI/Button",
  component: Button,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta

type Story = StoryObj<typeof Button>

export const Primary: Story = {
  args: { children: "Get started" },
}

export const Outline: Story = {
  args: { children: "View docs", variant: "outline" },
}

export const Ghost: Story = {
  args: { children: "Cancel", variant: "ghost" },
}

export const Destructive: Story = {
  args: { children: "Delete", variant: "destructive" },
}
