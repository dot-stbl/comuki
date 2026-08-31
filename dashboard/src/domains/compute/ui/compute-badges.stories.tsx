import type { Meta, StoryObj } from "@storybook/react"

import type { ProviderState } from "@/domains/compute/model/types"

import { ProviderKindMark, ProviderStateBadge } from "./compute-badges"

const STATES: ProviderState[] = ["active", "standby", "draining", "unreachable"]

const meta: Meta<typeof ProviderStateBadge> = {
  title: "Compute/Provider badges",
  component: ProviderStateBadge,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ProviderStateBadge>

/** All four states together — the only way to check the silhouettes differ. */
export const States: Story = {
  render: () => (
    <div style={{ display: "flex", gap: "var(--s4)", flexWrap: "wrap" }}>
      {STATES.map((state) => (
        <ProviderStateBadge key={state} state={state} />
      ))}
    </div>
  ),
}

/** `unreachable` is the one that escalates itself, on the kit's `failed` rule. */
export const Unreachable: Story = {
  args: { state: "unreachable" },
}

/**
 * Kind is the backend's own mark, with no badge chrome and no hue: which
 * implementation a provider is has no urgency, and a hairline box around a logo
 * would be building a container for a fact with no state in it. The word is
 * still there — as the mark's accessible name and as its hover reading.
 */
export const Kinds: Story = {
  render: () => (
    <div style={{ display: "flex", gap: "var(--s4)" }}>
      <ProviderKindMark kind="docker" />
      <ProviderKindMark kind="kubernetes" />
    </div>
  ),
}
