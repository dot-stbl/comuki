import type { Meta, StoryObj } from "@storybook/react"

import type { VirtualKey } from "@/domains/models/model/types"

import { EndpointStateBadge, KeyStateBadge, WireBadge } from "./model-badges"

const DAY = 86_400

function key(overrides: Partial<VirtualKey>): VirtualKey {
  return {
    id: "vk_story",
    prefix: "vk_story…",
    label: "a key",
    endpointId: "ep_a",
    models: ["worker-sm-4"],
    scope: { kind: "platform" },
    budgetUsd: 100,
    spentUsd: 10,
    expiresInSec: 30 * DAY,
    lastUsedAgoSec: DAY,
    revoked: false,
    ...overrides,
  }
}

const meta: Meta<typeof EndpointStateBadge> = {
  title: "Models/Registry badges",
  component: EndpointStateBadge,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof EndpointStateBadge>

/** Three endpoint states together — the only way to check the shapes differ. */
export const EndpointStates: Story = {
  render: () => (
    <div style={{ display: "flex", gap: "var(--s4)" }}>
      <EndpointStateBadge state="ok" />
      <EndpointStateBadge state="degraded" />
      <EndpointStateBadge state="disabled" />
    </div>
  ),
}

/**
 * A key's state is derived, not stored: it lapses on its own clock, and a
 * revocation overrides that. The badge takes the key so it and the table can
 * never disagree about which rule won.
 */
export const KeyStates: Story = {
  render: () => (
    <div style={{ display: "flex", gap: "var(--s4)" }}>
      <KeyStateBadge entry={key({})} />
      <KeyStateBadge entry={key({ expiresInSec: -3 * DAY })} />
      <KeyStateBadge entry={key({ revoked: true })} />
      {/* Both true at once: the deliberate act is the one shown. */}
      <KeyStateBadge entry={key({ expiresInSec: -DAY, revoked: true })} />
    </div>
  ),
}

/** The wire is an identity rather than a state, so it carries no hue. */
export const Wires: Story = {
  render: () => (
    <div style={{ display: "flex", gap: "var(--s4)" }}>
      <WireBadge wire="openai" />
      <WireBadge wire="anthropic" />
    </div>
  ),
}
