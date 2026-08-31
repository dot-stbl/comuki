import { useCallback, useMemo } from "react"

import type { SearchTarget } from "@/app/search"
import {
  useChatCommandsQuery,
  useChatSessionsQuery,
  useProposalDecisionMutation,
  useSendMessageMutation,
  useStartSessionMutation,
} from "@/domains/chat/api/queries"
import { availableCommands } from "@/domains/chat/model/commands"
import type { ProposalDecision } from "@/domains/chat/model/types"
import { ChatComposer } from "@/domains/chat/ui/chat-composer"
import { ChatSessions } from "@/domains/chat/ui/chat-sessions"
import { ChatSidePanel } from "@/domains/chat/ui/chat-side-panel"
import { ChatThread } from "@/domains/chat/ui/chat-thread"
import { useSession } from "@/shared/session"

import styles from "./chat-console.module.css"

export interface ChatConsoleProps {
  /**
   * Which conversation is open, held by the **container** rather than here.
   *
   * The console is mounted in two containers — the `/chat` route and the
   * dock's sheet — and the sheet is closed and reopened while somebody is
   * mid-thought. A conversation is state the *operator* is in, not state of
   * whichever box is showing it, so the container holds it and hands it back:
   * closing the sheet on a conversation and reopening it lands on the same
   * one, the way the route's own docblock promises a terminal does.
   */
  chosenId: string | null
  onChosenIdChange: (sessionId: string | null) => void
  /** The half-typed message, held by the container for the same reason. */
  draft: string
  onDraftChange: (next: string) => void
  /**
   * A reference the console was opened with — what the operator was looking
   * at when they reached for it. A suggestion, not a decision: the composer
   * shows it as a chip that leaves in one gesture, and the id rides along
   * with the next message only until that message is sent.
   */
  seed?: SearchTarget | null
  onSeedChange?: (next: SearchTarget | null) => void
}

/**
 * The console itself — one thread, one composer, one proposal card, one set
 * of tool-call records, in **two containers**.
 *
 * The `/chat` route and the dock's bottom sheet both render this component.
 * Not a copy, not a trimmed variant: the same element tree, because the day
 * two implementations of the console disagree, the operator believes the
 * wrong one — a state change confirmed in one of them is a state change
 * either way, and the product's contract is that console actions land in the
 * same journal the screens write to. A second console is a second path into
 * that journal, and this file is the proof there is no second one.
 *
 * ## What the container owns
 *
 * Everything that must outlive this tree: which conversation is open, the
 * draft, the seeded reference. The route holds them in screen state; the
 * dock holds them in a memory that survives the sheet closing. Everything
 * else — the queries, the mutations, the three columns — is the console's
 * own and identical in both places.
 *
 * ## The three columns
 *
 * The conversations on the left, the thread with the composer under it in
 * the middle, and an optional panel on the right reading out what is waiting
 * on a decision. The panel is the first thing to go when the box narrows —
 * it is a convenience, and the thread is the screen. Under the breakpoint
 * the same happens; a sheet on a narrow board and the route on a narrow
 * window behave as one thing.
 */
export function ChatConsole({
  chosenId,
  onChosenIdChange,
  draft,
  onDraftChange,
  seed,
  onSeedChange,
}: ChatConsoleProps) {
  const session = useSession()
  const sessions = useChatSessionsQuery()
  const custom = useChatCommandsQuery()

  const send = useSendMessageMutation()
  const decide = useProposalDecisionMutation()
  const start = useStartSessionMutation()

  const rows = useMemo(() => sessions.data ?? [], [sessions.data])
  const current = rows.find((entry) => entry.id === chosenId) ?? rows[0] ?? null

  const commands = useMemo(
    () => availableCommands(session, custom.data ?? []),
    [session, custom.data]
  )

  const onSend = useCallback(
    (text: string, projectId?: string) => {
      if (!current) {
        return
      }
      send.mutate({ sessionId: current.id, text, projectId })
    },
    [current, send]
  )

  const onDecide = useCallback(
    (proposalId: string, decision: ProposalDecision) => {
      if (!current) {
        return
      }
      decide.mutate({ sessionId: current.id, proposalId, decision })
    },
    [current, decide]
  )

  const onStart = useCallback(() => {
    start.mutate(undefined, {
      onSuccess: (created) => onChosenIdChange(created.id),
    })
  }, [start, onChosenIdChange])

  return (
    <div className={styles.screen} data-test="chat-console">
      <div className={styles.rail}>
        <ChatSessions
          sessions={rows}
          currentId={current?.id ?? null}
          onSelect={onChosenIdChange}
          onStart={onStart}
          busy={start.isPending}
        />
      </div>

      <div className={styles.centre}>
        <ChatThread
          messages={current?.messages ?? []}
          onDecide={onDecide}
          busy={decide.isPending}
        />
        <ChatComposer
          commands={commands}
          onSend={onSend}
          busy={send.isPending || !current}
          value={draft}
          onValueChange={onDraftChange}
          seed={seed}
          onSeedChange={onSeedChange}
        />
      </div>

      <div className={styles.panel}>
        <ChatSidePanel messages={current?.messages ?? []} commands={commands} />
      </div>
    </div>
  )
}
