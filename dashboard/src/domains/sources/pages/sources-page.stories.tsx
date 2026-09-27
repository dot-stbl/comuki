import type { Meta, StoryObj } from "@storybook/react"

import { sourcesQueryKey } from "@/domains/sources/api/queries"
import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { SourcesPage } from "./sources-page"

/* Where work comes from. Every connection on the platform — trackers,
   ticket systems, webhooks — and the decisions that are decisions about a
   row, not edits of one.

   Mock-first: the registry reads the mutable seed store. A story that
   says "three connections, one native" actually has the rows the operator
   would see if they had just signed in. The two stories below differ in
   the `focus` the address bar carried. */

const ROUTES = [
  "/",
  "/tasks",
  "/runs",
  "/queue",
  "/approvals",
  "/cost",
  "/sources",
  "/sources/new",
  "/sources/$sourceId",
  "/sources/$sourceId/ticket/new",
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

const meta: Meta<typeof SourcesPage> = {
  title: "Pages/Sources",
  component: SourcesPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof SourcesPage>

/**
 * The registry in full: every connection on the platform, with the
 * project it belongs to, the host it speaks to and the state it is in. The
 * decisions a row owns (test, disconnect) ride on the row because answering
 * either on a page the operator had to travel to would lose their place in
 * the list they are working down.
 */
export const Registry: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/sources"
      projects={PROJECTS}
    >
      <SourcesPage />
    </PageStoryFrame>
  ),
}

/**
 * Arrived at the page with a focus — the address bar said `?q=…`, so the
 * toolbar opens narrowed to a project. The chip says where the narrowing
 * came from, and clearing it returns to the full list.
 */
export const RegistryFocused: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/sources"
      projects={PROJECTS}
    >
      <SourcesPage focus="plexor" />
    </PageStoryFrame>
  ),
}

/**
 * A platform with no connections yet, pinned honestly: the sources cache
 * is seeded with an empty snapshot — no connections, no tickets — before
 * the page mounts. The page answers with its first-run reading, the one
 * that points at the connect flow, because an empty registry is a call to
 * action rather than a blank table.
 */
export const FirstRun: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/sources"
      projects={PROJECTS}
      seed={(client) => {
        client.setQueryData(sourcesQueryKey, {
          connections: [],
          tickets: [],
        })
      }}
    >
      <SourcesPage />
    </PageStoryFrame>
  ),
}