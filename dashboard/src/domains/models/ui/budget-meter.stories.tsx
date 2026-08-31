import type { Meta, StoryObj } from "@storybook/react"

import type { VirtualKey } from "@/domains/models/model/types"

import { BudgetMeter } from "./budget-meter"

const DAY = 86_400

function key(overrides: Partial<VirtualKey> = {}): VirtualKey {
  return {
    id: "vk_story",
    prefix: "vk_story…",
    label: "a key",
    endpointId: "ep_a",
    models: ["worker-sm-4"],
    scope: { kind: "platform" },
    budgetUsd: 400,
    spentUsd: 88.1,
    expiresInSec: 30 * DAY,
    lastUsedAgoSec: DAY,
    revoked: false,
    ...overrides,
  }
}

const meta: Meta<typeof BudgetMeter> = {
  title: "Models/Budget meter",
  component: BudgetMeter,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  // The meter fills a table cell, so it is shown at a cell's width rather than
  // at the page's — a bar measured against the wrong axis is not the component.
  decorators: [
    (Story) => (
      <div style={{ inlineSize: "11rem" }}>
        <Story />
      </div>
    ),
  ],
  args: { enforced: true },
}

export default meta
type Story = StoryObj<typeof BudgetMeter>

/** Room to spare: no hue, because there is nothing here to decide. */
export const Ok: Story = {
  args: { entry: key() },
}

/** Past 85%: a decision — raise the cap, or let the traffic stop. */
export const NearTheCap: Story = {
  args: { entry: key({ spentUsd: 361.4 }) },
}

/** Over. The bar cannot grow further, so the figures carry the reading. */
export const OverTheCap: Story = {
  args: { entry: key({ spentUsd: 431.2 }) },
}

/**
 * The proxy is off. The fraction is still true — the spend was real — but the
 * fill is hatched, because a solid bar claims something happens at the end of
 * it, and with the proxy off nothing will.
 */
export const NotEnforced: Story = {
  args: { entry: key({ spentUsd: 361.4 }), enforced: false },
}

/** A key that has stopped: it spends nothing more, whatever its bar says. */
export const Expired: Story = {
  args: { entry: key({ spentUsd: 120, expiresInSec: -3 * DAY }) },
}
