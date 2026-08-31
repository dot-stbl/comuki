import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { toRunSummary } from "@/domains/runs/api/mappers"
import { buildProfileFlow } from "@/domains/runs/model/profile-flow"
import { RUNS_SEED } from "@/shared/api/mock"

import { ProfileRiver, ProfileStrip } from "./profile-river"

const fullSwarm = buildProfileFlow(RUNS_SEED.map(toRunSummary))
const smallSwarm = buildProfileFlow(RUNS_SEED.slice(0, 4).map(toRunSummary))
const emptySwarm = buildProfileFlow([])

/** Selection is the table's `profile` filter, so nothing is pressed to begin with. */
function Interactive({ flow }: { flow: typeof fullSwarm }) {
  const [selected, setSelected] = useState<string | null>(null)
  return (
    <ProfileRiver
      flow={flow}
      selected={selected}
      onSelect={(profile) =>
        setSelected((current) => (current === profile ? null : profile))
      }
    />
  )
}

function Strip({ flow }: { flow: typeof fullSwarm }) {
  return (
    <div style={{ height: "var(--h-strip)" }}>
      <ProfileStrip flow={flow} onExpand={() => {}} />
    </div>
  )
}

const meta: Meta<typeof ProfileRiver> = {
  title: "Runs/Profile river",
  component: ProfileRiver,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ProfileRiver>

/** The load the screen is designed for: a full shift, the worst profile marked. */
export const FullSwarm: Story = {
  render: () => <Interactive flow={fullSwarm} />,
}

/** Four runs. The flow has to stay legible when the swarm is nearly idle, and
 *  with four plans the observed pipeline is genuinely shorter than a full one. */
export const SmallSwarm: Story = {
  render: () => <Interactive flow={smallSwarm} />,
}

/** No runs — the river renders no columns at all. */
export const Empty: Story = {
  render: () => <Interactive flow={emptySwarm} />,
}

/** A pressed profile is the table's profile filter, said a second way. */
export const ProfileSelected: Story = {
  render: () => (
    <ProfileRiver flow={fullSwarm} selected="implementer" onSelect={() => {}} />
  ),
}

/** The board collapsed: the same flow, one row tall, no numbers, no labels. */
export const Collapsed: Story = {
  render: () => <Strip flow={fullSwarm} />,
}
