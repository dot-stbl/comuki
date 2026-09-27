import type { Meta, StoryObj } from "@storybook/react"

import { runsQueryKey } from "@/domains/runs/api/queries"
import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { RunsPage } from "./runs-page"

/* The duty list — live runs, the screen visited most. The header's
   profile river is the first thing the operator reads; the table underneath
   is the second.

   Mock-first: the runs read the mutable seed store, so the page renders
   whatever rows the seed happens to hold. The mutations ride the same
   store — cancelling here settles the row exactly as it would in the app. */

const ROUTES = [
  "/",
  "/tasks",
  "/runs",
  "/runs/$runId",
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
  { id: "p_plexor", key: "plexor", name: "Plexor" },
] as const

const meta: Meta<typeof RunsPage> = {
  title: "Pages/Runs",
  component: RunsPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RunsPage>

/**
 * The full duty list: the river at the top names what the swarm is
 * working on, the table underneath is the rows the river came from. A
 * profile node the operator presses narrows the table to that profile —
 * the river and the table are two views of the same list, and the page
 * keeps them in step.
 */
export const DutyListFull: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/runs"
      projects={PROJECTS}
    >
      <RunsPage />
    </PageStoryFrame>
  ),
}

/**
 * An empty swarm, pinned honestly: the runs cache is seeded with an empty
 * list before the page mounts, so the river has no columns to draw and the
 * table renders its own "no runs in flight" reading — a sentence the page
 * says on purpose, not a blank screen that leaves the operator guessing
 * whether it loaded.
 */
export const EmptySwarm: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/runs"
      projects={PROJECTS}
      seed={(client) => {
        client.setQueryData(runsQueryKey, [])
      }}
    >
      <RunsPage />
    </PageStoryFrame>
  ),
}