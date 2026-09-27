import type { Meta, StoryObj } from "@storybook/react"

import { approvalsQueryKey } from "@/domains/approvals/api/queries"
import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { ApprovalsPage } from "./approvals-page"

/* The approvals queue — a stack of cards the duty engineer works through.
   Each card is a decision the swarm is waiting on, and the queue's whole
   job is to make the next decision the cheapest act on the page.

   Mock-first: the queue holds the seed store, so the queue renders
   however many cards the seed says it holds. The mutation rides the same
   store — confirming a card here settles the queue exactly as it would in
   the app. */

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

const meta: Meta<typeof ApprovalsPage> = {
  title: "Pages/Approvals",
  component: ApprovalsPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ApprovalsPage>

/**
 * The full queue: every card carries its subject, its project, and the
 * decision that is owed. The operator works top to bottom; the band on a
 * card is the act itself, not a navigation to one.
 */
export const QueueFull: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/approvals"
      projects={PROJECTS}
    >
      <ApprovalsPage />
    </PageStoryFrame>
  ),
}

/**
 * Nothing waiting on a person, pinned honestly: the approvals cache is
 * seeded empty before the page mounts. The page says so in its own words
 * rather than drawing a blank region — a decided queue is a quiet queue,
 * and the operator should read that as the day's state, not as a fault.
 */
export const NothingWaiting: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/approvals"
      projects={PROJECTS}
      seed={(client) => {
        client.setQueryData(approvalsQueryKey, [])
      }}
    >
      <ApprovalsPage />
    </PageStoryFrame>
  ),
}