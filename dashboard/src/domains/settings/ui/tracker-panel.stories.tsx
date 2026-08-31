import type { Meta, StoryObj } from "@storybook/react"

import type { TrackerProvider } from "@/domains/settings/model/types"

import { TrackerPanel } from "./tracker-panel"

const TRACKERS: TrackerProvider[] = [
  {
    id: "jira",
    name: "Jira",
    connected: true,
    meta: "project COMUKI · 14 issues",
    last: "2 min ago",
  },
  {
    id: "github",
    name: "GitHub Issues",
    connected: false,
    meta: "connect to import issues",
  },
  {
    id: "linear",
    name: "Linear",
    connected: false,
    meta: "connect to import issues",
  },
]

const meta: Meta<typeof TrackerPanel> = {
  title: "Settings/Tracker panel",
  component: TrackerPanel,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof TrackerPanel>

/**
 * Where tickets come from before anybody types one.
 *
 * A tracker is a data surface, not a card: a hairline on its start edge, the
 * lane material it is made of, and the surface step of the corner scale. The
 * connected one marks its own edge rather than growing a ring, because a ring
 * in this product is a focus state.
 */
export const Allowed: Story = {
  args: { trackers: TRACKERS, edit: { allowed: true, denial: null } },
}

/**
 * The same panel for a role that may not turn a live setting. Both acts stay
 * exactly where they were and say what is missing — `denied`, never `disabled`,
 * because a disabled control fires no pointer events and its tooltip would be
 * unreachable.
 */
export const Denied: Story = {
  args: {
    trackers: TRACKERS,
    edit: {
      allowed: false,
      denial: "needs project-admin, operator or platform-admin",
    },
  },
}

/** Nothing wired up yet. Manual intake still works, and the notice says so. */
export const NothingConnected: Story = {
  args: {
    trackers: TRACKERS.filter((tracker) => !tracker.connected),
    edit: { allowed: true, denial: null },
  },
}
