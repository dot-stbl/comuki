import type { Meta, StoryObj } from "@storybook/react"

import { PageStoryFrame } from "@/shared/storybook/page-story-frame"

import { ChatPage } from "./chat-page"

/* The console as a screen. One of the two containers the same `ChatConsole`
   component renders in: the dock's modal bottom sheet is the other, and the
   rule between them is that they draw the same component. A conversation, a
   draft and a decision are the same thing wherever they are shown, and two
   implementations of either would be two paths into one journal.

   The harness (`PageStoryFrame`) carries the router, the session and the
   query client — the three the app hands every screen — so the story only
   names the routes it cares about and the entry it wants the rail to land
   on. `form-page.stories.tsx` is the reference this follows.

   Mock-first: the console reads scripted sessions from the chat seed. A
   story that says "two open conversations, one with a proposal waiting"
   actually has those threads and proposals the operator can click on.
   Confirming a proposal here writes through to the run store, exactly as
   it would in the app. */

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
  "/chat",
  "/chat/init",
] as const

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
] as const

const meta: Meta<typeof ChatPage> = {
  title: "Pages/Chat",
  component: ChatPage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ChatPage>

/**
 * The console as a screen: the conversation list on the left, the thread
 * and the composer on the right, and the header link to the onboarding
 * wizard. A story that says "two open conversations" actually has two
 * threads the operator can click into; a story that says "one is waiting
 * on a decision" actually has one proposal the operator can confirm.
 */
export const TwoOpenConversations: Story = {
  render: () => (
    <PageStoryFrame
      routes={ROUTES}
      initialPath="/chat"
      projects={PROJECTS}
    >
      <ChatPage />
    </PageStoryFrame>
  ),
}