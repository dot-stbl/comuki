import type { Meta, StoryObj } from "@storybook/react"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { ProjectsPage } from "./projects-page"

/* The platform registry — every project, what it is running, what it costs.
   The lower tier of the rail, visited on a different clock from the duty
   screens and usually to create something or to answer what a project is
   spending.

   Mock-first: the registry reads the mutable seed store, joined with what
   each project is doing today. The two stories below differ in the `focus`
   the address bar carried — one arrived unfiltered, the other opened
   narrowed to a single project. */

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
  "/projects/new",
  "/projects/$projectId",
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

const meta: Meta<typeof ProjectsPage> = {
  title: "Pages/Projects",
  component: ProjectsPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ProjectsPage>

/**
 * The platform registry in full: every project, what it is running, what
 * it is spending. The summary figures in the header — the count, the
 * in-flight runs, the total spend today — are derived from the rows below
 * them, so the header and the list cannot disagree about what the platform
 * is doing.
 */
export const Registry: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/projects"
      projects={PROJECTS}
    >
      <ProjectsPage />
    </PageStoryFrame>
  ),
}

/**
 * Arrived at the page with a focus — the address bar said `?q=comuki`, so
 * the toolbar opens narrowed to the platform itself. The row says where
 * the list came from without saying so out loud: the operator can clear
 * the chip and see the rest, or leave it and trust that the page knows
 * why they were sent here.
 */
export const ArrivedNarrowed: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/projects"
      projects={PROJECTS}
    >
      <ProjectsPage focus="comuki" />
    </PageStoryFrame>
  ),
}