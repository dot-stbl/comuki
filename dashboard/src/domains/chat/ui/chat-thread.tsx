import {
  Fragment,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type UIEvent as ReactUIEvent,
} from "react"
import { useVirtualizer } from "@tanstack/react-virtual"
import { ArrowDown } from "lucide-react"

import { messageParts } from "@/domains/chat/model/parts"
import type {
  ChatMessage as Message,
  ProposalDecision,
} from "@/domains/chat/model/types"
import { Button } from "@/shared/ui"

import { ChatMessage } from "./chat-message"
import { renderPart } from "./message-part"

import styles from "./chat-thread.module.css"

export interface ChatThreadProps {
  messages: Message[]
  onDecide: (proposalId: string, decision: ProposalDecision) => void
  busy?: boolean
}

/**
 * The messages, and the three things a thread gets wrong by default.
 *
 * ## 1. The politeness, decided rather than defaulted
 *
 * A streaming reply that pushes every token into a live region is unusable: a
 * screen reader reads the same growing sentence from the start, over and over,
 * and there is no gesture that stops it. The usual fix — `aria-live="off"` on
 * the thread — is worse, because then nothing is announced at all and the
 * operator has to go looking for the answer they asked for.
 *
 * So the thread is split in two, and the split is the whole design:
 *
 * - **The log** is `role="log"` with `aria-live="polite"` and
 *   `aria-relevant="additions"`. It holds every message that has *finished*.
 *   A finished message is announced once, when it is added, and never again —
 *   which is exactly the reading a log wants. `polite` and not `assertive`:
 *   nothing the console says is worth interrupting a sentence the operator is
 *   already listening to.
 * - **The reply in flight** is rendered *outside* the log, and is
 *   `aria-hidden`. Its text is changing several times a second and there is no
 *   politeness setting that makes that bearable. Instead a single
 *   `role="status"` says, once, that a reply is arriving — and when it lands it
 *   enters the log like every other message and is read out in full, once.
 *
 * The net effect for somebody listening: "the assistant is replying", a pause,
 * then the answer. For somebody looking: the text arrives as it is written.
 * Neither reading is a degraded version of the other.
 *
 * ## 2. The scroll follows the operator, not the data
 *
 * `scrollTop = scrollHeight` on every change is the default every chat ships
 * with, and it fights the one person it is for: an operator who scrolled up to
 * read what a tool returned is dragged back to the bottom by the next token.
 *
 * So the thread is **pinned only while it is already at the bottom**. The
 * pin is a ref rather than state, because it is read inside the effect that
 * acts on it and a render in between would be a render that scrolls a frame
 * late. When the pin is off, the new messages land silently and a **jump to
 * latest** control appears — the thing that was missing entirely, and the only
 * honest way to take the scroll away from somebody: offer it back.
 *
 * ## 3. Virtualization, past the point where it is worth it
 *
 * A thread of twelve turns is not a performance problem, and virtualizing it
 * costs something real: rows enter and leave the DOM as the operator scrolls,
 * and a row entering a live region is an *addition*, which is the one thing
 * this log is configured to announce. So virtualization starts at
 * {@link VIRTUALIZE_FROM} turns — past the length where scrolling back is
 * browsing history rather than re-reading an answer, and past the length where
 * a thousand mounted markdown trees are what makes the composer feel slow.
 *
 * Under the threshold the log is the plain list it has always been. Over it,
 * the window is drawn between two spacers whose heights stand for everything
 * not mounted, so the scrollbar still measures the whole conversation. Rows
 * are measured rather than estimated — a turn can be one line or a
 * sixty-line patch, and an estimate would make the scrollbar lie.
 *
 * ## The height chain
 *
 * `.thread` is a flex column with `min-block-size: 0` inside the page's grid
 * cell, and `.scroll` is `flex: 1 1 0` with `overflow-y: auto`. The composer
 * beneath is `flex: 0 0 auto`. jsdom computes none of this, so it is
 * hand-traced in `chat-page.module.css` from `.shell` down.
 */
export function ChatThread({ messages, onDecide, busy }: ChatThreadProps) {
  const scroll = useRef<HTMLDivElement | null>(null)

  /**
   * Whether the port is at the bottom, held twice on purpose.
   *
   * The ref is what the scroll effect reads — it has to be current at the
   * moment the effect runs, and state is a render behind. The state is what
   * the jump control is rendered from. They are written together and nothing
   * else reads either.
   */
  const pinned = useRef(true)
  const [atBottom, setAtBottom] = useState(true)

  // A reply in flight is by definition the newest thing in the thread, so the
  // split is a partition rather than a search through the middle of the list.
  const { settled, pending } = useMemo(() => {
    const last = messages[messages.length - 1]
    return last?.streaming
      ? { settled: messages.slice(0, -1), pending: last }
      : { settled: messages, pending: null }
  }, [messages])

  const virtualize = settled.length >= VIRTUALIZE_FROM

  // The virtualizer's getters read live scroll state, so they are meant to be
  // re-read every render rather than memoized — the same opt-out the kit's
  // data table takes, and for the same reason.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    // Zero under the threshold, so the hook stays mounted (hooks are not
    // conditional) and costs nothing: no items, no measurement, no observer.
    count: virtualize ? settled.length : 0,
    getScrollElement: () => scroll.current,
    estimateSize: () => ESTIMATED_TURN,
    getItemKey: (index) => settled[index]?.id ?? index,
    overscan: OVERSCAN,
  })

  const toBottom = useCallback(
    (smooth: boolean) => {
      const port = scroll.current
      if (!port) {
        return
      }
      if (smooth && typeof port.scrollTo === "function") {
        port.scrollTo({ top: port.scrollHeight, behavior: "smooth" })
        return
      }
      port.scrollTop = port.scrollHeight
    },
    // `scroll` is a ref; nothing here changes between renders.
    []
  )

  useEffect(() => {
    if (!pinned.current) {
      return
    }
    // Never smooth on an arrival: a reply lands token by token, and an
    // animated scroll restarted several times a second is the thread
    // vibrating. Smooth belongs to the one gesture a human made — the jump.
    toBottom(false)
  }, [messages, toBottom])

  const onScroll = useCallback(
    (event: ReactUIEvent<HTMLDivElement>) => {
      const port = event.currentTarget
      const distance = port.scrollHeight - port.scrollTop - port.clientHeight
      const near = distance <= NEAR_BOTTOM
      pinned.current = near
      if (near !== atBottom) {
        setAtBottom(near)
      }
    },
    [atBottom]
  )

  const jump = useCallback(() => {
    pinned.current = true
    setAtBottom(true)
    toBottom(prefersMotion())
  }, [toBottom])

  // Named `shown` rather than `window`: this file reads the real `window` for
  // the motion preference, and shadowing it here would be a trap for whoever
  // next needs it.
  const shown = virtualize ? virtualizer.getVirtualItems() : []
  const first = shown[0]
  const last = shown[shown.length - 1]
  const above = first ? first.start : 0
  const below = last ? virtualizer.getTotalSize() - last.end : 0
  const drawn = virtualize
    ? shown.map((item) => ({ index: item.index, message: settled[item.index] }))
    : settled.map((message, index) => ({ index, message }))

  return (
    <div className={styles.thread} data-test="chat-thread">
      <div
        className={styles.scroll}
        data-test="chat-scroll"
        ref={scroll}
        onScroll={onScroll}
      >
        {settled.length === 0 && !pending ? (
          <div className={styles.empty} data-test="chat-empty">
            <h2 className={styles.emptyTitle}>Nothing said yet</h2>
            <p className={styles.emptyBody}>
              This is the same control plane the screens drive, reached by
              typing. Ask it what the swarm is doing, or start with a slash to
              see everything it can do. Anything that would change something
              comes back as a proposal you press — it never acts on its own.
            </p>
          </div>
        ) : null}

        <ol
          className={styles.log}
          data-test="chat-log"
          data-virtualized={virtualize || undefined}
          role="log"
          aria-live="polite"
          aria-relevant="additions"
          aria-label="Conversation"
        >
          {/* The two spacers stand for every turn that is not mounted, so the
              scrollbar measures the conversation rather than the window. They
              are `<li>`s because an `<ol>` may only hold list items, and
              hidden because they are geometry rather than content. */}
          {above > 0 ? (
            <li
              className={styles.spacer}
              aria-hidden="true"
              style={{ "--spacer": `${above}px` } as CSSProperties}
            />
          ) : null}

          {drawn.map(({ index, message }) =>
            message ? (
              <ChatMessage
                key={message.id}
                message={message}
                onDecide={onDecide}
                busy={busy}
                data-index={virtualize ? index : undefined}
                ref={virtualize ? virtualizer.measureElement : undefined}
              />
            ) : null
          )}

          {below > 0 ? (
            <li
              className={styles.spacer}
              aria-hidden="true"
              style={{ "--spacer": `${below}px` } as CSSProperties}
            />
          ) : null}
        </ol>

        {pending ? (
          /* Outside the log, and hidden from assistive technology while the
             tokens arrive. It joins the log — and is read once, in full — the
             moment it settles. */
          <div
            className={styles.pending}
            data-test="chat-streaming"
            aria-hidden="true"
          >
            <div className={styles.pendingByline}>
              <span className={styles.pendingAuthor}>comuki</span>
              <span className={styles.pendingClock}>{pending.at}</span>
            </div>
            {/* The same parts the settled reading draws, so nothing changes
                shape when it lands — and `MessageProse` sees `streaming`, so
                the prose is not parsed as markdown until it is finished. Half
                a document is a different document. */}
            {messageParts(pending).map((part, index) => (
              <Fragment key={index}>{renderPart(part, pending)}</Fragment>
            ))}
            <span className={styles.cursor} />
          </div>
        ) : null}
      </div>

      {/* The scroll was taken away from the operator the moment they scrolled
          up; this is how it is offered back. Present only when it would do
          something — a control that is always there and usually inert is a
          control nobody reads. */}
      {atBottom ? null : (
        <Button
          className={styles.jump}
          variant="outline"
          size="sm"
          data-test="chat-jump"
          onClick={jump}
        >
          <ArrowDown aria-hidden="true" />
          <span>jump to latest</span>
        </Button>
      )}

      {/* One announcement per reply, at the start of it. Empty the rest of the
          time, so nothing is repeated when the thread re-renders. */}
      <p className={styles.announce} role="status" data-test="chat-announce">
        {pending ? "the assistant is replying" : ""}
      </p>
    </div>
  )
}

/**
 * How many settled turns before the log starts virtualizing.
 *
 * Sixty is where a conversation stops being something re-read and starts being
 * something scrolled through, and it is comfortably above the longest seeded
 * thread — so the ordinary console, and every test of it, runs the plain list
 * and is never at the mercy of a measurement jsdom cannot make.
 */
const VIRTUALIZE_FROM = 60

/**
 * The height a turn is assumed to have before it has been measured.
 *
 * Only ever a starting point: every mounted row reports its real height back
 * through `measureElement`, because a turn in this console is anything from
 * one line to a sixty-line patch and no single estimate is right twice.
 */
const ESTIMATED_TURN = 96

/** Rows drawn beyond the window, so a fast scroll does not show blank space. */
const OVERSCAN = 8

/**
 * How close to the end still counts as the end.
 *
 * Generous on purpose: a thread that unpins the instant a wheel moves by a
 * pixel is a thread that stops following the conversation for no reason the
 * operator would recognise as a decision they made.
 */
const NEAR_BOTTOM = 64

/** Whether this room asked for motion. The jump is the only place that asks. */
function prefersMotion(): boolean {
  return (
    typeof window !== "undefined" &&
    typeof window.matchMedia === "function" &&
    window.matchMedia("(prefers-reduced-motion: no-preference)").matches
  )
}
