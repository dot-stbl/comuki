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
 * ## Portal content — tagged "ws16-portal", included in "ws16-batch1"
 *
 * `BottomSheet` renders through react-aria-components' `ModalOverlay`/
 * `Modal`, which portals its children into `document.body` rather than
 * Storybook's own `#storybook-root`. That used to keep these stories out of
 * `test:storybook`'s "ws16-batch1" entirely: `@storybook/test-runner@0.23.0`'s
 * own per-story transition (`channel.emit("setCurrentStory", ...)` on an
 * already-loaded preview) never signalled ready for them — even the plain
 * `FillingTheWindow` story, which has no play function at all, timed out at
 * Jest's default 15s.
 *
 * WS16.4 (`.storybook/test-runner.ts`'s `PORTAL_TAG`/`preVisit`) works around
 * it: a story tagged `"ws16-portal"` gets pre-rendered via a direct
 * navigation to its own `iframe.html?id=...` URL instead — the same
 * technique WS17's `ui:probe` already used successfully against this exact
 * component (`bun run ui:probe -- --story domains-chat-chatdock--panel-depth`).
 * `postVisit`'s a11y/visual capture also scopes to `document.body` instead
 * of `#storybook-root` for a tagged story, so it actually sees the portaled
 * composer/seed chip. See `storybook-tests/README.md` "Portal-based
 * stories" for the full account; every other kit primitive built on
 * `Modal`/`Dialog` (`ConfirmDialog`, `FormDialog`, `Dialog` itself) can use
 * the same tag once it needs this harness.
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
  // "ws16-batch1": test:storybook's first interaction/visual/a11y batch —
  // see storybook-tests/README.md. "ws16-portal": this story's content
  // portals into `document.body` (BottomSheet/Modal) — see the docblock
  // above and .storybook/test-runner.ts's `PORTAL_TAG`.
  tags: ["ws16-batch1", "ws16-portal"],
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
