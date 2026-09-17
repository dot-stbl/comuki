import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { RotateCw } from "lucide-react"

import type { SearchTarget } from "@/app/search"
import {
  useChatCommandsQuery,
  useChatMessagesQuery,
  useChatSessionsQuery,
  useProposalDecisionMutation,
  useSendMessageMutation,
  useStartSessionMutation,
} from "@/domains/chat/api/queries"
import { availableCommands } from "@/domains/chat/model/commands"
import { beginChatTurn } from "@/domains/chat/model/streaming"
import type { ProposalDecision } from "@/domains/chat/model/types"
import { ChatComposer } from "@/domains/chat/ui/chat-composer"
import { ChatSessions } from "@/domains/chat/ui/chat-sessions"
import { ChatSidePanel } from "@/domains/chat/ui/chat-side-panel"
import { ChatThread } from "@/domains/chat/ui/chat-thread"
import { useChatTurnStream } from "@/domains/chat/ui/use-chat-turn-stream"
import { requestFailureMessage } from "@/shared/api/problem"
import { useSession } from "@/shared/session"
import { Button, ScreenState, Skeleton, Tooltip } from "@/shared/ui"

import styles from "./chat-console.module.css"

/* The thread that has not arrived yet, as turns of uneven length. Uneven on
   purpose: a column of equal bars reads as a loaded thread full of blanks. */
const SKELETON_WIDTHS = ["64%", "42%", "78%", "51%", "70%", "38%"]

/**
 * An act the console was asked to do and could not.
 *
 * Stamped with the conversation it was aimed at, because the console keeps
 * running while the operator switches rails: an error raised against
 * yesterday's thread must not appear over today's, where it would name a
 * failure that never happened here. `sessionId: null` is an act that was not
 * about a conversation at all — starting one — and those are shown wherever
 * the operator is.
 */
interface ConsoleFailure {
  sessionId: string | null
  /** The wire's own sentence. */
  message: string
  /**
   * The words that did not go, when the box could not take them back. Null
   * whenever they were returned to the composer, which is the ordinary case.
   */
  unsent: string | null
}

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
  /**
   * Focus the composer the moment this tree mounts. The dock's sheet mounts
   * and unmounts with every open and close, so the box is ready to type in
   * the instant the sheet arrives; the route mounts once on navigation and
   * leaves the focus where the operator put it.
   */
  focusComposerOnMount?: boolean
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
  focusComposerOnMount,
}: ChatConsoleProps) {
  const session = useSession()
  const sessions = useChatSessionsQuery()
  const custom = useChatCommandsQuery()

  const send = useSendMessageMutation()
  const decide = useProposalDecisionMutation()
  const start = useStartSessionMutation()

  const rows = useMemo(() => sessions.data ?? [], [sessions.data])
  /* The held id first, then the newest conversation, then nothing. The fall
     through is deliberate and silent: the dock hands back an id from before a
     navigation and the route from before a reload, and an id that is no longer
     in the list is almost always one the host retired rather than one the
     operator is looking for. Saying "that conversation is gone" every time a
     stale memory is handed back would make the console apologise for working.
     The reading stays honest because the rail marks which row is current. */
  const current = rows.find((entry) => entry.id === chosenId) ?? rows[0] ?? null

  /**
   * The open conversation's transcript, asked for by itself.
   *
   * The wire's session row carries no messages — the host pages them behind
   * `GET /api/v1/chat/sessions/{id}/messages` — so a console that read the
   * thread off the session list showed an empty thread against a real
   * backend, forever. The fallback to the session's own `messages` is what
   * keeps mock mode identical: there the messages live on the record, the
   * query reads the same store, and the two can only ever agree.
   */
  const transcript = useChatMessagesQuery(current?.id ?? "")
  const messages = useMemo(
    () => transcript.data ?? current?.messages ?? [],
    [transcript.data, current]
  )

  /**
   * The live half of a running turn: the just-sent row plus the streamed
   * fragments, drawn on top of the transcript until the turn settles. The
   * overlay rows are the console's optimistic answer to a POST that takes
   * tens of seconds — without them the console looks deaf between send and
   * reply; with them the operator watches the brain think.
   */
  const overlay = useChatTurnStream(current?.id ?? null)
  const shown = useMemo(() => {
    if (!overlay.user && !overlay.reply) {
      return messages
    }
    const withUser = overlay.user ? [...messages, overlay.user] : messages
    return overlay.reply ? [...withUser, overlay.reply] : withUser
  }, [messages, overlay])

  const commands = useMemo(
    () => availableCommands(session, custom.data ?? []),
    [session, custom.data]
  )

  const [failure, setFailure] = useState<ConsoleFailure | null>(null)

  /* What is in the box *now*, readable from a callback that runs later. The
     `draft` a send closed over is the message being sent; by the time the
     wire answers, the operator may have typed something else entirely, and
     the decision below turns on which of the two is newer. */
  const draftRef = useRef(draft)
  useEffect(() => {
    draftRef.current = draft
  }, [draft])

  const onSend = useCallback(
    (text: string, projectId?: string) => {
      if (!current) {
        return
      }
      const sessionId = current.id
      setFailure(null)
      // The optimistic rows exist before the POST leaves — the send must be
      // visible the instant the operator presses enter, not when the whole
      // brain turn comes back.
      beginChatTurn(sessionId, text)
      send.mutate(
        { sessionId, text, projectId },
        {
          /* The composer empties on the gesture, because a box that holds the
             words until a round trip finishes feels broken on every send that
             works. The price of that is this branch: a refused send has to
             give the words back, or a message the operator typed is gone with
             nothing on screen to say it ever existed.

             Given back to the box only when the box is empty. If the operator
             kept typing while the request was in flight, that thought is
             newer than this one, and pasting the old message over it would be
             a second loss to repair the first — so the words ride in the
             notice instead, where they can still be read and copied. */
          onError: (error) => {
            const returned = draftRef.current.trim().length === 0
            if (returned) {
              onDraftChange(text)
            }
            setFailure({
              sessionId,
              message: requestFailureMessage(error, "The message was not sent"),
              unsent: returned ? null : text,
            })
          },
        }
      )
    },
    [current, send, onDraftChange]
  )

  const onDecide = useCallback(
    (proposalId: string, decision: ProposalDecision) => {
      if (!current) {
        return
      }
      const sessionId = current.id
      setFailure(null)
      decide.mutate(
        { sessionId, proposalId, decision },
        {
          // A proposal whose confirm does nothing is the worst reading this
          // console can give: the operator believes the act landed in the
          // journal, and it did not.
          onError: (error) => {
            setFailure({
              sessionId,
              message: requestFailureMessage(
                error,
                "The decision was not recorded"
              ),
              unsent: null,
            })
          },
        }
      )
    },
    [current, decide]
  )

  const onStart = useCallback(() => {
    setFailure(null)
    start.mutate(undefined, {
      onSuccess: (created) => onChosenIdChange(created.id),
      // Not about a conversation — there is no conversation — so it carries no
      // stamp and is shown wherever the operator happens to be.
      onError: (error) => {
        setFailure({
          sessionId: null,
          message: requestFailureMessage(
            error,
            "The conversation was not started"
          ),
          unsent: null,
        })
      },
    })
  }, [start, onChosenIdChange])

  // The last thing *the operator* said in this conversation, for the empty
  // box's arrow-up. Derived, not stored: the thread is the history, and a
  // second copy of it would be a second thing to keep true.
  const recall = useMemo(() => {
    for (let index = messages.length - 1; index >= 0; index -= 1) {
      if (messages[index]?.kind === "person") {
        return messages[index]?.text ?? null
      }
    }
    return null
  }, [messages])

  // A failed read is a state, not an empty console. `data ?? []` on the
  // queries below is what mock mode needs (the store cannot fail), but in
  // real mode a swallowed error renders "no conversations" for a dead wire
  // — indistinguishable from the truth and therefore a lie. The sessions
  // failure takes the whole column (nothing else on the console can be
  // true without the list); a transcript failure with nothing to fall
  // back to takes the thread's place. Both offer the same one retry the
  // pages give (runs, tasks, cost).
  const sessionsFailed = sessions.isError
  const transcriptFailed =
    !sessionsFailed && transcript.isError && messages.length === 0

  // Loading is a state, and it is not the empty one. Without it the thread
  // draws "Nothing said yet" over a conversation that is on its way — the
  // console telling the operator the answer is nothing while it is still
  // asking the question. The skeleton stands where the turns will be; the
  // transcript's own load only counts while there is nothing to keep showing,
  // so a poll refetch never blanks a thread that is already readable.
  const loading =
    sessions.isLoading || (transcript.isLoading && messages.length === 0)

  /* Shown only where it happened. A failure carrying another conversation's
     stamp belongs to that conversation, and an unstamped one — a conversation
     that could not be started — belongs to none and is shown anywhere. */
  const shownFailure =
    failure && (failure.sessionId === null || failure.sessionId === current?.id)
      ? failure
      : null

  return (
    <div className={styles.screen} data-test="chat-console">
      <div className={styles.rail}>
        <ChatSessions
          sessions={rows}
          currentId={current?.id ?? null}
          onSelect={onChosenIdChange}
          onStart={onStart}
          busy={start.isPending}
          loading={sessions.isLoading}
          /* The centre already says the console did not load, and says it with
             the one retry. A rail answering the same dead read with "no
             conversations yet" would be a second and wrong reading of it. */
          failed={sessionsFailed}
        />
      </div>

      <div className={styles.centre}>
        {sessionsFailed ? (
          <ScreenState
            kind="error"
            title="The console did not load"
            description={requestFailureMessage(sessions.error, "Unknown error")}
            inset="gutter"
            data-test="chat-console-error"
            action={
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="chat-console-error-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void sessions.refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            }
          />
        ) : transcriptFailed ? (
          <ScreenState
            kind="error"
            title="The transcript did not load"
            description={requestFailureMessage(
              transcript.error,
              "Unknown error"
            )}
            inset="gutter"
            data-test="chat-transcript-error"
            action={
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="chat-transcript-error-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void transcript.refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            }
          />
        ) : loading ? (
          <Skeleton
            lines={SKELETON_WIDTHS}
            inset="gutter"
            fill
            label="Loading the conversation"
            data-test="chat-console-loading"
          />
        ) : (
          <>
            <ChatThread
              messages={shown}
              onDecide={onDecide}
              busy={decide.isPending}
              projectId={current?.projectId ?? null}
              awaiting={send.isPending}
            />
            <ChatComposer
              commands={commands}
              onSend={onSend}
              busy={send.isPending || !current}
              value={draft}
              onValueChange={onDraftChange}
              seed={seed}
              onSeedChange={onSeedChange}
              recall={recall}
              autoFocus={focusComposerOnMount}
              failure={
                shownFailure
                  ? {
                      message: shownFailure.message,
                      unsent: shownFailure.unsent,
                    }
                  : null
              }
            />
          </>
        )}
      </div>

      <div className={styles.panel}>
        <ChatSidePanel messages={messages} commands={commands} />
      </div>
    </div>
  )
}
