import { createContext, useContext, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router"

import type { ChatMessage as Message } from "@/domains/chat/model/types"
import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider, type SessionUser } from "@/shared/session"

import { ChatMessage } from "./chat-message"
import { ChatThread } from "./chat-thread"

/* Messages carry real `<Link>`s — a typed reference resolves through the
   product's own shape catalogue — and ask a real permission per proposal, so
   they only render inside a router and a session. A memory router with the
   product's own paths gives the links somewhere to go. */

const SlotContext = createContext<ReactNode>(null)

function Slot() {
  return <>{useContext(SlotContext)}</>
}

const rootRoute = createRootRoute({ component: Slot })
const blank = () => null
const routeTree = rootRoute.addChildren(
  ["/", "/chat", "/runs", "/runs/$runId", "/queue", "/tasks", "/identity"].map(
    (path) =>
      createRoute({ getParentRoute: () => rootRoute, path, component: blank })
  )
)

const router = createRouter({
  routeTree,
  history: createMemoryHistory({ initialEntries: ["/chat"] }),
})

/** Watches every project and decides on none — every proposal explains itself. */
const WATCHER: SessionUser = {
  id: "u_watch",
  name: "Watcher",
  email: "watch@comuki.local",
  platformRoles: ["viewer"],
  projectRoles: {},
}

function Frame({
  children,
  user = SESSION_USER_SEED,
}: {
  children: ReactNode
  user?: SessionUser
}) {
  return (
    <SessionProvider user={user} projects={PROJECTS_SEED}>
      <SlotContext
        value={
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              padding: "var(--s6)",
              maxInlineSize: "56rem",
            }}
          >
            {children}
          </div>
        }
      >
        <RouterProvider router={router} />
      </SlotContext>
    </SessionProvider>
  )
}

function One({ message, user }: { message: Message; user?: SessionUser }) {
  return (
    <Frame user={user}>
      <ol style={{ listStyle: "none", margin: 0, padding: 0 }}>
        <ChatMessage message={message} onDecide={() => {}} />
      </ol>
    </Frame>
  )
}

const meta: Meta<typeof ChatMessage> = {
  title: "Chat/ChatMessage",
  component: ChatMessage,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ChatMessage>

/**
 * **State 1 — streaming.** The only message with no fixed end. It is rendered
 * outside the thread's `role="log"` and hidden from assistive technology while
 * the tokens arrive; a single `role="status"` says a reply is coming, and the
 * finished text is announced once when it lands. See `chat-thread.tsx`.
 */
export const Streaming: Story = {
  render: () => (
    <Frame>
      <div style={{ blockSize: "14rem" }}>
        <ChatThread
          messages={[
            {
              id: "m1",
              kind: "person",
              text: "9d72b5f0 упал на третьем шаге. что произошло",
              at: "08:04",
            },
            {
              id: "m2",
              kind: "reply",
              streaming: true,
              at: "08:05",
              text: "Шаг w3 умер сразу после установки зависимостей: воркер импортирует theme/v1, а в новом пакете этого пути больше нет. Судя по трассе, план писался ещё",
            },
          ]}
          onDecide={() => {}}
        />
      </div>
    </Frame>
  ),
}

/**
 * **State 2 — a tool call.** A record rather than a spinner: the endpoint, the
 * arguments it went out with, and what came back. Two of them here, because
 * the failed one is the state that usually goes missing — an operator who
 * cannot see which call failed cannot tell a wrong answer from a broken one.
 */
export const ToolCalls: Story = {
  render: () => (
    <Frame>
      <ol style={{ listStyle: "none", margin: 0, padding: 0 }}>
        <ChatMessage
          message={{
            id: "m1",
            kind: "tool",
            at: "09:12",
            tool: {
              name: "runs.get",
              args: "run=8f3c2a91",
              status: "success",
              result:
                "status=running · current=w4 · profile=implementer · 4m12s · $0.42",
            },
          }}
          onDecide={() => {}}
        />
        <ChatMessage
          message={{
            id: "m2",
            kind: "tool",
            at: "09:12",
            tool: {
              name: "queue.workers",
              args: "image=sha256:9a41c0",
              status: "failed",
              result:
                "504 from the compute provider after 30s — pool unreachable",
            },
          }}
          onDecide={() => {}}
        />
      </ol>
    </Frame>
  ),
}

/**
 * **State 3 — a plan awaiting a decision.** The only message with a decision on
 * it. Two controls that keep their words, because they are the two halves of
 * one question. Nothing here can act on its own: there is no effect, no timer
 * and no default answer.
 */
export const Proposal: Story = {
  render: () => (
    <One
      message={{
        id: "m1",
        kind: "proposal",
        at: "09:14",
        proposal: {
          id: "cp_plan",
          act: "plan.approve",
          summary:
            "approve the deploy gate on 5b1d7e40 and release the run back to the swarm",
          projectId: "p_comuki",
          subject: "5b1d7e40",
          steps: [
            { profile: "verifier", label: "дождаться аппрува на раскатку" },
            { profile: "docs", label: "записать решение в базу знаний" },
          ],
        },
      }}
    />
  ),
}

/**
 * **State 4 — permission denied.** Not a message kind at all: it is the same
 * proposal, seen by a shift that may not confirm it. The controls stay exactly
 * where they were, take `aria-disabled`, carry the sentence on `data-denied`
 * and swallow the click — never `disabled`, which fires no pointer events and
 * would put the explanation out of reach. The reason is also on the page, not
 * only in a tooltip.
 */
export const PermissionDenied: Story = {
  render: () => (
    <One
      user={WATCHER}
      message={{
        id: "m1",
        kind: "proposal",
        at: "08:51",
        proposal: {
          id: "cp_plan_plexor",
          act: "plan.approve",
          summary:
            "approve the rollback gate on 2a6f1c33 and release the run back to the swarm",
          projectId: "p_plexor",
          subject: "2a6f1c33",
          steps: [
            { profile: "verifier", label: "подтвердить, что откат готов" },
          ],
        },
      }}
    />
  ),
}

/**
 * **State 5 — error.** The turn failed, and the console says which part of it
 * did rather than apologising in general. The one message that announces
 * itself as an alert, marked by a rule and a glyph as well as a hue.
 */
export const Errored: Story = {
  render: () => (
    <One
      message={{
        id: "m1",
        kind: "error",
        at: "06:40",
        text: "The lead model gave up on this turn: the cost roll-up it needs has not been built for the window you asked about, and it has no second way to answer.",
      }}
    />
  ),
}

/**
 * A reply with a typed reference and a hand-off. `5b1d7e40` resolves through
 * the product's own shape catalogue and becomes a link to the run; the
 * question that would have been a list becomes a filter on the real screen,
 * from the same resolver the command palette uses.
 */
export const ReferencesAndHandoff: Story = {
  render: () => (
    <One
      message={{
        id: "m1",
        kind: "reply",
        at: "09:13",
        text: "Прогон 8f3c2a91 живой: воркер стоит на шаге w4. Соседний прогон 5b1d7e40 ждёт человека на аппруве раскатки.",
        handoff: "webhook",
      }}
    />
  ),
}

/** A decided proposal is history: it keeps its shape and loses its question. */
export const Decided: Story = {
  render: () => (
    <One
      message={{
        id: "m1",
        kind: "proposal",
        at: "09:15",
        proposal: {
          id: "cp_done",
          act: "run.stop",
          summary: "stop 2a6f1c33 and tear down the worker container",
          projectId: "p_comuki",
          subject: "2a6f1c33",
          decision: "confirmed",
        },
      }}
    />
  ),
}
