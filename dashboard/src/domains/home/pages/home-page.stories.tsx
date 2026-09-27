import type { Meta, StoryObj } from "@storybook/react"

import { runsQueryKey } from "@/domains/runs/api/queries"
import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { HomePage } from "./home-page"

/* The home screen is the operator's first stop — the page that answers
   "am I needed right now, and where". The harness (`PageStoryFrame`)
   carries the router, the session and the query client; the query reads
   the seed store, and a story that needs a different shift pins the
   cache rather than rewriting the store — the client is built with
   `staleTime: Infinity` and seeded once, so the query never refetches and
   the page truthfully reads what the story installed. */

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
  "/runs/$runId",
] as const

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
] as const

const meta: Meta<typeof HomePage> = {
  title: "Pages/Home",
  component: HomePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof HomePage>

/**
 * The home screen as it reads on a shift — the verdict names the figure,
 * the list shows the runs behind it, and the band below says what the
 * swarm did last week. The page renders one column and scrolls: the
 * verdict, then the runs that owe a decision, then the running tail, then
 * shortcuts. A quiet shift and a busy one look different only because the
 * seed is different — the page does not branch on the count.
 */
export const OnShift: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/"
      projects={PROJECTS}
    >
      <HomePage />
    </PageStoryFrame>
  ),
}

/**
 * A quiet shift, honestly held: the runs cache is pinned empty before the
 * page mounts, so the verdict reads "nobody needs me" because the data says
 * so — not because the store happened to be reset. The outcomes band still
 * carries the week's history, which is true of a quiet shift too: the swarm
 * worked, it just has nothing waiting on a person right now.
 */
export const QuietShift: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/"
      projects={PROJECTS}
      seed={(client) => {
        client.setQueryData(runsQueryKey, [])
      }}
    >
      <HomePage />
    </PageStoryFrame>
  ),
}