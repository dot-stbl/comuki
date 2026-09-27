import type { ReactElement } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import type { KnowledgeTab } from "@/domains/knowledge/model/tabs"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { KnowledgePage } from "./knowledge-page"

/* What the swarm has been told, and what telling it that did. The page is
   read-only: rules live in the client's git and change by commit. The one
   exception is the verification gate, folded in from the screen that used
   to stand at `/verify`, which carries its own permission.

   Mock-first: the library reads the seed store, the gate reads its own
   panel set. A story that says "ten rules, six golden tasks" actually has
   ten rows the operator can click on.

   A tab change inside the story is local — the URL is the router's, and the
   story lives without one — so the page receives the active tab as a prop.
   This is the contract the route itself honours (`?tab=`), and the page
   reads no router state on its own. */

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

const meta: Meta<typeof KnowledgePage> = {
  title: "Pages/Knowledge",
  component: KnowledgePage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof KnowledgePage>

function storyOf(tab: KnowledgeTab, focus?: string): ReactElement {
  return (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/knowledge"
      projects={PROJECTS}
    >
      <KnowledgePage tab={tab} focus={focus} onTabChange={() => {}} />
    </PageStoryFrame>
  )
}

/** The library — every rule, doc and skill the swarm has been told about,
 *  and the revisions they ride. The list is the answer to "what is in
 *  force"; the search field above narrows it without leaving the screen. */
export const Library: Story = { render: () => storyOf("library") }

/** Arrived at the library with a focus — the address bar said `?q=…`, so
 *  the search opens narrowed to the named rule. The chip says where the
 *  narrowing came from. */
export const LibraryFocused: Story = {
  render: () => storyOf("library", "checkout"),
}

/** The verification gate — folded in from the screen that used to stand at
 *  `/verify`. The same chassis as the library (read-only registry sourced
 *  from the client's git, project-scoped), so it sits behind the same
 *  door with its own tab rather than claiming a share of the page. */
export const Gate: Story = { render: () => storyOf("gate") }