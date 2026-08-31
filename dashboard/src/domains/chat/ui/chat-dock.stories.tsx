import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"

import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider } from "@/shared/session"
import { BottomSheet } from "@/shared/ui"

import { ChatConsole } from "./chat-console"

/* The dock's sheet states, composed the way `ChatDock` composes them: the
 * kit's `BottomSheet` carrying the one console both containers share. The
 * trigger's own state lives in the dock (open on click, seed from the
 * location), so the stories state it directly instead.
 *
 * Serves the mock seeds — the console's queries are the real ones, and they
 * need `VITE_USE_MOCK=true`, which `.env.example` documents and this
 * worktree's `.env.local` provides.
 */

function Frame({ children }: { children: ReactNode }) {
  const rootRoute = createRootRoute({
    component: () => <>{children}</>,
  })
  const blank = () => null
  const routeTree = rootRoute.addChildren(
    [
      "/",
      "/chat",
      "/runs",
      "/runs/$runId",
      "/queue",
      "/tasks",
      "/identity",
    ].map((path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
    )
  )
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/chat"] }),
  })

  return (
    <SessionProvider user={SESSION_USER_SEED} projects={PROJECTS_SEED}>
      <QueryClientProvider client={new QueryClient()}>
        {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
        <RouterProvider router={router as any} />
      </QueryClientProvider>
    </SessionProvider>
  )
}

interface SheetStoryProps {
  expanded?: boolean
  seed?: boolean
}

function SheetStory({ expanded = false, seed = false }: SheetStoryProps) {
  return (
    <Frame>
      <BottomSheet
        open
        onOpenChange={() => {}}
        title="Console"
        storageKey="comuki.story.chat-dock"
        expanded={expanded}
        onExpandedChange={() => {}}
      >
        <ChatConsole
          chosenId={null}
          onChosenIdChange={() => {}}
          draft={seed ? "почему он стоит" : ""}
          onDraftChange={() => {}}
          seed={
            seed
              ? {
                  kind: "run",
                  id: "5b1d7e40",
                  href: "/runs/5b1d7e40",
                  permission: "runs.view",
                }
              : null
          }
          onSeedChange={() => {}}
        />
      </BottomSheet>
    </Frame>
  )
}

const meta = {
  title: "Domains/Chat/ChatDock",
  component: SheetStory,
  parameters: { layout: "fullscreen" },
} satisfies Meta<typeof SheetStory>

export default meta

type Story = StoryObj<typeof meta>

export const PanelDepth: Story = {}

export const FillingTheWindow: Story = {
  args: { expanded: true },
}

export const SeededFromARun: Story = {
  args: { seed: true },
}
