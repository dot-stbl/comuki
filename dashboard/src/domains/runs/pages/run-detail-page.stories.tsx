import type { Meta, StoryObj } from "@storybook/react"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { RunDetailPage } from "./run-detail-page"

/* One run, in full: its record, its plan graph, its journal. The page is the
   second question a duty engineer asks after the runs list — what is this
   one doing, and what is the swarm waiting on to do it next.

   Mock-first: the run reads the mutable seed store, so a story that names
   a run id actually has that run. Confirming a proposal here writes
   through to the same store the duty list reads. */

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

const meta: Meta<typeof RunDetailPage> = {
  title: "Pages/Run detail",
  component: RunDetailPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RunDetailPage>

/**
 * One run in flight: the plan graph laid out by depth, the journal under
 * it, the items and their dependencies. The story names the run id the
 * way the URL does — `/runs/<runId>` — and the page renders whatever the
 * seed holds for that id.
 */
export const InFlight: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/runs"
      projects={PROJECTS}
    >
      <RunDetailPage runId="8f3c2a91" />
    </PageStoryFrame>
  ),
}

/**
 * A run that failed outright. The seed holds it under `9d72b5f0`, and the
 * page renders a terminal run the way it renders every other — the plan
 * graph with the failed items marked, the journal explaining what gave,
 * and no live sections pretending the run is still moving.
 */
export const FailedRun: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/runs"
      projects={PROJECTS}
    >
      <RunDetailPage runId="9d72b5f0" />
    </PageStoryFrame>
  ),
}

/**
 * An address that outlived its run. The id resolves to nothing in the seed,
 * the query answers `null` — an ordinary miss, not a load failure — and the
 * page draws its own missing reading, naming the id it was given because
 * that is the only part of a dead link the operator can take back to
 * whoever wrote it.
 */
export const Missing: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/runs"
      projects={PROJECTS}
    >
      <RunDetailPage runId="run_gone" />
    </PageStoryFrame>
  ),
}