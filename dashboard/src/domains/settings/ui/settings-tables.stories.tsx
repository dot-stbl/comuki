import type { Meta, StoryObj } from "@storybook/react"

import { toSettingsSnapshot } from "@/domains/settings/api/mappers"
import { SETTINGS_SEED } from "@/shared/api/mock/settings.seed"

import { AppsPanel } from "./apps-panel"
import { AutonomyPanel } from "./autonomy-panel"
import { KeysPanel } from "./keys-panel"
import { RulesPanel } from "./rules-panel"

const snapshot = toSettingsSnapshot(SETTINGS_SEED)

const meta: Meta<typeof AppsPanel> = {
  title: "Settings/Panels",
  component: AppsPanel,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof AppsPanel>

/**
 * The app registry. Read-only, and the line under the title says why rather
 * than leaving the operator to work it out from the absence of controls: the
 * registry is declared in the client's own repository, so changing it is a
 * commit over there.
 */
export const Apps: Story = {
  args: { apps: snapshot.apps },
}

/**
 * The swarm rule set, with the conflict reading above the table rather than
 * under it — two rules whose scopes overlap is how a swarm ends up arguing
 * with itself, and finding that out after scrolling five rows is late.
 */
export const Rules: StoryObj<typeof RulesPanel> = {
  render: () => <RulesPanel rules={snapshot.rules} />,
}

/**
 * What the swarm decides alone. Recorded gap: this panel reads and does not
 * write, and unlike Apps, Rules and Keys that is not because its source is git
 * — §12.1 lists "plan approve on/off" as a live setting, so the row that ought
 * to carry a switch carries a badge instead.
 */
export const Autonomy: StoryObj<typeof AutonomyPanel> = {
  render: () => <AutonomyPanel rows={snapshot.autonomy} />,
}

/**
 * Provider keys. The last column is what the panel is for: a key at
 * `budget 67%` is the reason a run will fail in three hours, said in the
 * provider's own words rather than as the enum behind them.
 */
export const Keys: StoryObj<typeof KeysPanel> = {
  render: () => <KeysPanel keys={snapshot.keys} />,
}
