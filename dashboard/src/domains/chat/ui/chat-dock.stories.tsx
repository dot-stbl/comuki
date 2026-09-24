import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { expect, fn, userEvent, waitFor } from "@storybook/test"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"

import type { SearchTarget } from "@/app/search"
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
 *
 * ## Known limitation — excluded from `test:storybook`'s "ws16-batch1"
 *
 * `BottomSheet` renders through react-aria-components' `ModalOverlay`/
 * `Modal`, which portals its children into `document.body` rather than
 * Storybook's own `#storybook-root`. The play functions below are correct
 * (verified manually: a direct Playwright visit renders the composer/seed
 * chip and both play functions pass), but `@storybook/test-runner@0.23.0`'s
 * own "has this story rendered" readiness check hangs indefinitely for a
 * story whose root element never gains children — even the plain
 * `FillingTheWindow` story, which has no play function at all, times out at
 * Jest's default 15s. This is a `#storybook-root`-emptiness problem in the
 * test-runner harness, not a bug in these stories or in `BottomSheet`.
 *
 * Every kit primitive built on `Modal`/`Dialog` (`ConfirmDialog`,
 * `FormDialog`, `Dialog` itself, `BottomSheet`) will hit the same wall.
 * Tracked as a WS16.4 follow-up — options to investigate: a newer
 * test-runner major (blocked on the SB10 migration this repo has
 * deliberately deferred, see `.storybook/main.ts`'s TODO(phase-7)), or a
 * custom `prepare`/readiness override in `.storybook/test-runner.ts` that
 * watches the Storybook channel's `STORY_RENDERED` event instead of DOM
 * mutations on `#storybook-root`.
 */

/** This repo's components key on `data-test`, not testing-library's default
 *  `data-testid` — see `chat-message.test.tsx`'s `at()` helper. `root` is
 *  `document`, not `canvasElement`: `BottomSheet` renders through
 *  react-aria-components' `ModalOverlay`/`Modal`, which portals into
 *  `document.body` — the composer and seed chip never appear inside
 *  Storybook's own root element at all. */
function byTest(root: ParentNode, name: string): HTMLElement {
  const found = root.querySelector<HTMLElement>(`[data-test="${name}"]`)
  if (!found) {
    throw new Error(`[data-test="${name}"] not found in document (BottomSheet portals to document.body)`)
  }
  return found
}

/** `ChatConsole` shows a loading skeleton until its (mock) queries settle,
 *  and only mounts the composer/seed chip after — poll instead of a single
 *  synchronous query, which races the story's own first render. */
async function waitForTest(root: ParentNode, name: string): Promise<HTMLElement> {
  return waitFor(() => byTest(root, name))
}

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

const RUN_SEED: SearchTarget = {
  kind: "run",
  id: "5b1d7e40",
  href: "/runs/5b1d7e40",
  permission: "runs.view",
}

interface SheetStoryProps {
  expanded?: boolean
  seed?: boolean
  onDraftChange?: (next: string) => void
  onSeedChange?: (next: SearchTarget | null) => void
}

function SheetStory({
  expanded = false,
  seed = false,
  onDraftChange = () => {},
  onSeedChange = () => {},
}: SheetStoryProps) {
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
          onDraftChange={onDraftChange}
          seed={seed ? RUN_SEED : null}
          onSeedChange={onSeedChange}
        />
      </BottomSheet>
    </Frame>
  )
}

const meta = {
  title: "Domains/Chat/ChatDock",
  component: SheetStory,
  parameters: { layout: "fullscreen" },
  // NOT tagged "ws16-batch1" (yet) — see the docblock below and
  // storybook-tests/README.md's "Known limitation" section.
  tags: [],
} satisfies Meta<typeof SheetStory>

export default meta

type Story = StoryObj<typeof meta>

export const PanelDepth: Story = {
  args: { onDraftChange: fn() },
  play: async ({ canvasElement, args }) => {
    const doc = canvasElement.ownerDocument
    const composer = await waitForTest(doc, "chat-input")
    const send = byTest(doc, "chat-send")
    await expect(composer).toBeInTheDocument()
    await expect(send).toBeInTheDocument()

    await userEvent.type(composer, "ping")
    await expect(args.onDraftChange).toHaveBeenCalled()
  },
}

export const FillingTheWindow: Story = {
  args: { expanded: true },
}

export const SeededFromARun: Story = {
  args: { seed: true, onSeedChange: fn() },
  play: async ({ canvasElement, args }) => {
    const chip = await waitForTest(canvasElement.ownerDocument, "chat-seed")
    await userEvent.click(chip)
    await expect(args.onSeedChange).toHaveBeenCalledWith(null)
  },
}
