import type { Meta, StoryObj } from "@storybook/react"

import { toRunSummary } from "@/domains/runs/api/mappers"
import { readAttention } from "@/domains/home/model/attention"
import { RUNS_SEED } from "@/shared/api/mock"

import { AttentionVerdict } from "./attention-verdict"

const shift = readAttention(RUNS_SEED.map(toRunSummary))

const meta: Meta<typeof AttentionVerdict> = {
  title: "Home/Attention verdict",
  component: AttentionVerdict,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof AttentionVerdict>

/**
 * The state this screen is in most of the day, and the one that has to be
 * designed hardest: a statement with a mark and live figures, never an empty
 * list. If this reads as "the data did not arrive", the screen has failed.
 */
export const NothingOwed: Story = {
  args: {
    count: 0,
    mix: [],
    worst: null,
    running: 41,
    queued: 12,
  },
}

/** No runs at all. Still nothing owed — the second line is what differs. */
export const EmptySwarm: Story = {
  args: { count: 0, mix: [], worst: null, running: 0, queued: 0 },
}

/** The seeded shift: a real mix, with the worst status setting the band. */
export const FullShift: Story = {
  args: {
    count: shift.items.length,
    mix: shift.mix,
    worst: shift.worst,
    running: shift.running.length,
    queued: shift.queued,
  },
}

/** One run, one decision — the copy goes singular rather than reading "1 runs". */
export const SingleDecision: Story = {
  args: {
    count: 1,
    mix: [{ status: "waiting", count: 1 }],
    worst: "waiting",
    running: 18,
    queued: 3,
  },
}

/** Nothing escalated: the band takes the worst status actually present. */
export const OnlyFailures: Story = {
  args: {
    count: 4,
    mix: [{ status: "failed", count: 4 }],
    worst: "failed",
    running: 22,
    queued: 6,
  },
}
