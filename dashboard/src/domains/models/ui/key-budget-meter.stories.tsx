import type { Meta, StoryObj } from "@storybook/react"

import type { VirtualKey } from "@/domains/models/model/types"

import { KeyBudgetMeter } from "./key-budget-meter"

const DAY = 86_400

function key(overrides: Partial<VirtualKey> = {}): VirtualKey {
  // Cast: the integrator added optional fields the story defaults don't carry yet.
  return {
    id: "vk_story",
    prefix: "vk_story…",
    label: "a key",
    endpointId: "ep_a",
    models: ["worker-sm-4"],
    scope: { kind: "platform" },
    budgetUsd: 400,
    spentUsd: 88.1,
    expiresInSec: 30, createdAgoSec: 0 * DAY,
    lastUsedAgoSec: DAY,
    revoked: false,
    createdAgoSec: 2 * DAY,
    grants: null,
    spendDaily: null,
    ...overrides,
  } as VirtualKey
}

const meta: Meta<typeof KeyBudgetMeter> = {
  title: "Models/Key budget meter",
  component: KeyBudgetMeter,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta

type Story = StoryObj<typeof KeyBudgetMeter>

export const NoCap: Story = {
  args: { enforced: true, entry: key({ budgetUsd: null, spentUsd: null }) },
}

export const UnderBudget: Story = {
  args: { enforced: true, entry: key() },
}

export const OverBudget: Story = {
  args: { enforced: true, entry: key({ budgetUsd: 80 }) },
}

export const Expired: Story = {
  args: { enforced: true, entry: key({ expiresInSec: -10 }) },
}

export const NotMetered: Story = {
  args: { enforced: true, entry: key({ spentUsd: null }) },
}
