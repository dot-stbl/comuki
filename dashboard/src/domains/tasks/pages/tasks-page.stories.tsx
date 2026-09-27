import type { Meta, StoryObj } from "@storybook/react"

import { tasksQueryKey } from "@/domains/tasks/api/queries"
import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { TasksPage } from "./tasks-page"

/* The tasks / inbox — the pending backlog the operator works through to
   launch work into the swarm. Each row is one ticket, the dispatch on a
   row is the act that hands it to the orchestrator.

   Mock-first: the inbox reads the mutable seed store. A story that says
   "four tickets waiting" actually has four rows the operator can dispatch.
   The mutation rides the same store — dispatching here settles the row
   exactly as it would in the app. The two stories below differ in the
   `focus` the address bar carried. */

const ROUTES = [
  "/",
  "/tasks",
  "/tasks/new",
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
  { id: "p_plexor", key: "plexor", name: "Plexor" },
] as const

const meta: Meta<typeof TasksPage> = {
  title: "Pages/Tasks",
  component: TasksPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof TasksPage>

/**
 * The full inbox — every ticket the swarm has not yet picked up, in the
 * order the queue hands them out. The dispatch is the act on the row, and
 * one click is one ticket handed to the orchestrator.
 */
export const InboxOpen: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/tasks"
      projects={PROJECTS}
    >
      <TasksPage />
    </PageStoryFrame>
  ),
}

/**
 * Arrived at the inbox with a focus — the address bar said `?q=…`, so the
 * toolbar opens narrowed. The chip says where the narrowing came from, and
 * clearing it returns to the full list.
 */
export const InboxNarrowed: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/tasks"
      projects={PROJECTS}
    >
      <TasksPage focus="checkout" />
    </PageStoryFrame>
  ),
}

/**
 * The inbox is empty, pinned honestly: the tasks cache is seeded with an
 * empty list before the page mounts, so the page says "no tickets waiting"
 * in its own words — a state worth saying out loud, because a screen that
 * drew nothing else would leave the operator wondering whether it had
 * loaded at all.
 */
export const InboxEmpty: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/tasks"
      projects={PROJECTS}
      seed={(client) => {
        client.setQueryData(tasksQueryKey, [])
      }}
    >
      <TasksPage />
    </PageStoryFrame>
  ),
}