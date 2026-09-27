import type { Meta, StoryObj } from "@storybook/react"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { CostPage } from "./cost-page"

/* Cost & failures — the slow-clock reading of what the swarm is doing with
   the budget. Three tiles across the top (today / forecast / budget) and
   the breakdowns underneath, in the order the operator reads them.

   Mock-first: the figures come from the cost seed, so the page renders
   today's snapshot. The `cost-mock-mark` strip at the bottom is the only
   thing the page draws that distinguishes mock from real, and it stays. */

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

const meta: Meta<typeof CostPage> = {
  title: "Pages/Cost",
  component: CostPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof CostPage>

/**
 * Today, in full. The three tiles across the top show where the day is
 * going; the breakdowns underneath name each shape the operator reads in a
 * glance — spend by day, by model, top projects, by app, and where runs
 * fail. The delta chip rides beside the period toggle: a number that says
 * whether today is hotter than the previous one, with the arrow a colour
 * and not a word.
 */
export const TodayInFull: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/cost"
      projects={PROJECTS}
    >
      <CostPage />
    </PageStoryFrame>
  ),
}