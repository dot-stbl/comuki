import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"

import { toApproval } from "@/domains/approvals/api/mappers"
import type { Approval } from "@/domains/approvals/model/types"
import { APPROVALS_SEED, PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { SessionProvider, type SessionUser } from "@/shared/session"

import { ApprovalCard } from "./approval-card"

/* The card reads the run list to draw a plan preview and asks a permission per
   row, so it only renders inside a query client and a session. Nothing here
   fetches — the plan preview is simply absent when the run list is empty, which
   is one of the states worth having a story for. */

const queue: Approval[] = APPROVALS_SEED.map(toApproval)

/** Watches every project and decides on none — every act explains itself. */
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
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  return (
    <SessionProvider user={user} projects={PROJECTS_SEED}>
      <QueryClientProvider client={client}>
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "var(--s4)",
            padding: "var(--s6)",
            maxInlineSize: "56rem",
          }}
        >
          {children}
        </div>
      </QueryClientProvider>
    </SessionProvider>
  )
}

const meta: Meta<typeof ApprovalCard> = {
  title: "Approvals/ApprovalCard",
  component: ApprovalCard,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ApprovalCard>

/** The queue as the shift on duty sees it — three kinds of decision, mixed. */
export const Queue: Story = {
  render: () => (
    <Frame>
      {queue.slice(0, 3).map((approval) => (
        <ApprovalCard
          key={approval.id}
          approval={approval}
          onAction={() => {}}
        />
      ))}
    </Frame>
  ),
}

/**
 * A refused decision keeps its place and says what is missing. `aria-disabled`
 * and never `disabled`: a disabled control fires no pointer events, so the
 * tooltip carrying the sentence would be unreachable — and the control would
 * leave the tab order as well. Reading the plan stays open to everyone, because
 * the person who cannot approve is often exactly the one asked why.
 */
export const Refused: Story = {
  render: () => (
    <Frame user={WATCHER}>
      <ApprovalCard approval={queue[0]} onAction={() => {}} />
    </Frame>
  ),
}

/** A decision already in flight: `disabled` here, because busy is not a denial. */
export const Busy: Story = {
  render: () => (
    <Frame>
      <ApprovalCard approval={queue[0]} busy onAction={() => {}} />
    </Frame>
  ),
}
