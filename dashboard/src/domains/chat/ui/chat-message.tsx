import { Fragment, useMemo, type Ref } from "react"
import { AlertTriangle } from "lucide-react"

import { messageParts } from "@/domains/chat/model/parts"
import type {
  ChatMessage as Message,
  ProposalDecision,
} from "@/domains/chat/model/types"

import { renderPart } from "./message-part"
import { ProposalCard } from "./proposal-card"

import styles from "./chat-message.module.css"

export interface ChatMessageProps {
  message: Message
  onDecide: (proposalId: string, decision: ProposalDecision) => void
  busy?: boolean
  /**
   * The thread's virtualizer measures the row it drew.
   *
   * Both of these are the virtualizer's contract and nothing else's: it needs
   * the element to observe and the index to file the measurement under. They
   * are absent under the virtualization threshold, which is where the thread
   * spends almost all of its life — see `chat-thread.tsx`.
   */
  ref?: Ref<HTMLLIElement>
  "data-index"?: number
}

/**
 * One turn in the thread, drawn from the parts it is made of.
 *
 * ## The composition, in two layers
 *
 * **The parts** are the turn's content — prose, code, a diagram, the model's
 * working-out, the call it made, the hand-off under it — and they are drawn by
 * a table keyed on the part union, in `ui/message-part.tsx`. There is no
 * conditional here for any of them, which is the point: a chain of `&&`s is
 * only ever wrong by omission and the table cannot be.
 *
 * **The chrome** is what the thread says *about* the turn rather than what the
 * turn said: who spoke and when, the error band, the proposal. Those two stay
 * here, because neither is something a model produced — an error is the
 * console reporting on itself, and a proposal is a control the product owns.
 * (A proposal becomes a `decision` part in P2; the union is already open to
 * it and this file loses a branch when it lands.)
 *
 * ## The states, and what makes each of them different
 *
 * | state | what makes it different |
 * | --- | --- |
 * | **streaming** | the only message with no fixed end. Rendered *outside* the log region while it arrives — see `chat-thread.tsx` — so a screen reader is told once that a reply is coming rather than once per token, and it is not parsed as markdown until it settles. |
 * | **tool call** | a record rather than a spinner: the endpoint, its arguments and what came back, with a status badge carrying hue *and* a mark. |
 * | **proposal** | the only message with a decision on it. Two controls that keep their words, and no path from rendering to acting. |
 * | **error** | the turn failed, and the console says which part of it did rather than apologising in general. It is the one message that announces itself as an alert. |
 * | **permission denied** | not a message kind at all — it is a proposal whose confirming control is refused, which is the point. The same shape, in the same place, with the reason on it. |
 * | **empty** | a turn whose body derived to nothing. It says so, once, in faint text. The composition used to render a bare `<li>` here — the hole a `tool` message with no tool record fell into — and a gap in a log that explains nothing is worse than an admission. |
 *
 * The author line names who spoke and when, in the data voice, because both are
 * values in a log. There are no avatars and no bubbles: a bubble is a card, and
 * a data surface here is bounded by a hairline and takes the corner its size
 * deserves.
 */
export function ChatMessage({
  message,
  onDecide,
  busy,
  ref,
  "data-index": dataIndex,
}: ChatMessageProps) {
  const mine = message.kind === "person"
  const parts = useMemo(() => messageParts(message), [message])
  const proposal = message.kind === "proposal" ? message.proposal : undefined
  const errored = message.kind === "error"
  const blank = parts.length === 0 && !proposal && !errored

  return (
    <li
      className={styles.message}
      ref={ref}
      data-test="chat-message"
      data-kind={message.kind}
      data-streaming={message.streaming || undefined}
      data-message={message.id}
      data-index={dataIndex}
    >
      <div className={styles.byline}>
        <span className={styles.author}>{mine ? "you" : "comuki"}</span>
        <span className={styles.clock}>{message.at}</span>
      </div>

      <div className={styles.body}>
        {errored ? (
          <p className={styles.error} data-test="chat-error" role="alert">
            <AlertTriangle className={styles.errorIcon} aria-hidden="true" />
            <span>{message.text}</span>
          </p>
        ) : null}

        {proposal ? (
          <ProposalCard
            proposal={proposal}
            onDecide={onDecide}
            busy={busy}
          />
        ) : null}

        {parts.map((part, index) => (
          /* Parts have no ids of their own — the wire contract is a list, and
             a synthetic id would be a second thing to keep true. The index is
             the honest key: a turn's parts are fixed the moment it is
             journaled, so the list is never reordered or spliced. */
          <Fragment key={index}>{renderPart(part, message)}</Fragment>
        ))}

        {blank ? (
          <p className={styles.blank} data-test="chat-blank">
            this turn arrived with nothing in it
          </p>
        ) : null}
      </div>
    </li>
  )
}
