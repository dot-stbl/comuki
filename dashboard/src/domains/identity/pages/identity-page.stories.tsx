import type { Meta, StoryObj } from "@storybook/react"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { IdentityPage } from "./identity-page"

/* Who exists, what they hold, what acts for them. Three lists that share a
   screen because none answers its own question.

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
  "/identity/users/new",
  "/identity/users/$userId",
  "/identity/users/$userId/link",
  "/identity/grants/new",
  "/identity/keys/new",
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

const meta: Meta<typeof IdentityPage> = {
  title: "Pages/Identity",
  component: IdentityPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof IdentityPage>

/** The users tab — every account the registry holds, with the platform
 *  role it carries and the project role it does not. The list is the door to
 *  account acts; the rest of the page is the door to what those accounts
 *  hold. */
export const UsersTab: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/identity"
      projects={PROJECTS}
    >
      <IdentityPage tab="users" onTabChange={() => {}} />
    </PageStoryFrame>
  ),
}

/** The grants tab — every grant the registry holds, on every project. A
 *  grant is illegible until you see whose it is; the row carries both. */
export const GrantsTab: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/identity"
      projects={PROJECTS}
    >
      <IdentityPage tab="grants" onTabChange={() => {}} />
    </PageStoryFrame>
  ),
}

/** The keys tab — every API key the registry holds, with the project it
 *  opens and whether it is still active. A revoked key is still on the
 *  list, in red, for the audit story a deleted key would erase. */
export const KeysTab: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/identity"
      projects={PROJECTS}
    >
      <IdentityPage tab="keys" onTabChange={() => {}} />
    </PageStoryFrame>
  ),
}

/** Arrived at the grants list with a focus — the address bar named the
 *  subject. The list opens narrowed, and the toolbar shows why: a chip with
 *  the seed value that says "this is why your list is short". */
export const GrantsFocused: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/identity"
      projects={PROJECTS}
    >
      <IdentityPage tab="grants" focus="rhea" onTabChange={() => {}} />
    </PageStoryFrame>
  ),
}