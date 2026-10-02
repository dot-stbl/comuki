import type { Meta, StoryObj } from "@storybook/react"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { ComputePage } from "./compute-page"

/* The platform compute registry — providers, pools, worker versions, boards.
   The mock catalogue behind `useComputeQuery` ships four providers
   (docker + three flavours of kubernetes) with two pools each and a stack
   of recent worker versions, so the screen lands with all four sections
   populated without any seeding. The boards section is gated on the
   `observability.view` platform permission — the session in
   `PageStoryFrame` carries `platform-admin`, so the section renders and
   the connect guide appears below it.

   The empty compute path needs `useComputeQuery` to resolve to `{ providers:
   [], pools: [], versions: [] }`, which `setQueryData` against the
   `computeQueryKey` does. The first story here is the realistic view; the
   second is the registry-empty reading, which is the path the screen
   promises but the harness cannot reach through mock seeding alone. */

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
] as const

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
] as const

const meta: Meta<typeof ComputePage> = {
  title: "Pages/Compute",
  component: ComputePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ComputePage>

/**
 * The registry in its populated reading — one provider is the active
 * taker, the pools are sized under their quota ceilings, and the recent
 * worker versions are stacked on the right. The boards section rides on
 * the same platform permission as the door, so it is part of this
 * surface for any session that opened the page.
 */
export const RegistryPopulated: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/compute"
      projects={PROJECTS}
    >
      <ComputePage />
    </PageStoryFrame>
  ),
}