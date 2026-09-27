import type { Meta, StoryObj } from "@storybook/react"

import {
  queueQueryKey,
  workersQueryKey,
} from "@/domains/queue/api/queries"
import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { QueuePage } from "./queue-page"

/* Queue & workers — the screen that joins the two halves a stuck swarm
   produces. A worker holding a lease it stopped defending is a worker the
   queue never noticed; an item nobody claims is a fault only the pool can
   explain.

   Mock-first: the queue half reads the seed store, the worker half reads
   its own. The page renders whatever rows the seeds happen to hold today. */

const ROUTES = [
  "/",
  "/tasks",
  "/runs",
  "/queue",
  "/approvals",
  "/cost",
  "/sources",
  "/knowledge",
  "/verify",
  "/settings",
  "/identity",
  "/projects",
  "/compute",
  "/models",
  "/observability",
  "/components",
] as const

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
] as const

const meta: Meta<typeof QueuePage> = {
  title: "Pages/Queue",
  component: QueuePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof QueuePage>

/**
 * The full board: items queued, depth by day, and the pool above. The
 * two halves share a screen because neither answers its own question —
 * "queued eleven minutes" is only a fault once you can see there was an
 * idle worker to take it.
 */
export const BoardFull: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/queue"
      projects={PROJECTS}
    >
      <QueuePage />
    </PageStoryFrame>
  ),
}

/**
 * A dormant board, pinned honestly: both caches — the queue items and the
 * worker registry — are seeded empty before the page mounts, so each half
 * draws its own empty reading. Nothing queued and nobody up is a real
 * state for an ephemeral swarm between shifts, and the page says it rather
 * than leaving two blank panels.
 */
export const DormantBoard: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/queue"
      projects={PROJECTS}
      seed={(client) => {
        client.setQueryData(queueQueryKey, {
          items: [],
          pools: [],
          depth: [],
        })
        client.setQueryData(workersQueryKey, [])
      }}
    >
      <QueuePage />
    </PageStoryFrame>
  ),
}