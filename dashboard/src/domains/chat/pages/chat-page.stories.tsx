import { createContext, useContext, useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import { SessionProvider } from "@/shared/session"

import { ChatPage } from "./chat-page"

/* The console as a screen. One of the two containers the same `ChatConsole`
   component renders in: the dock's modal bottom sheet is the other, and the
   rule between them is that they draw the same component. A conversation, a
   draft and a decision are the same thing wherever they are shown, and two
   implementations of either would be two paths into one journal.

   The crumbs are real `<Link>`s and the shell draws the whole rail, so the
   screen only renders inside a router, a session and a query client — the
   same three the app hands it. A memory router carrying the product's own
   paths gives the story working crumbs without dragging in the generated
   route tree; `form-page.stories.tsx` is the harness this follows.

   Mock-first: the console reads scripted sessions from the chat seed. A
   story that says "two open conversations, one with a proposal waiting"
   actually has those threads and proposals the operator can click on.
   Confirming a proposal here writes through to the run store, exactly as
   it would in the app. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null

const routeTree = rootRoute.addChildren(
  [
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
  ].map((path) =>
    createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/chat"] }),
})

const PROJECTS = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
]

function Frame({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: { queries: { retry: false, staleTime: Infinity } },
      })
  )

  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Rhea Okafor",
        email: "rhea@comuki.local",
        platformRoles: ["platform-admin"],
        projectRoles: {},
      }}
      projects={PROJECTS}
    >
      <QueryClientProvider client={client}>
        <SlotContext value={children}>
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          <RouterProvider router={router as any} />
        </SlotContext>
      </QueryClientProvider>
    </SessionProvider>
  )
}

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
export const Default: Story = {
  render: () => (
    <Frame>
      <ChatPage />
    </Frame>
  ),
}
