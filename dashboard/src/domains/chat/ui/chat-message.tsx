import { AlertTriangle } from "lucide-react"

import type {
  ChatMessage as Message,
  ProposalDecision,
} from "@/domains/chat/model/types"

import { ChatHandoffs } from "./chat-handoff"
import { MessageText } from "./message-text"
import { ProposalCard } from "./proposal-card"
import { ToolCallCard } from "./tool-call"

import styles from "./chat-message.module.css"

export interface ChatMessageProps {
  message: Message
  onDecide: (proposalId: string, decision: ProposalDecision) => void
  busy?: boolean
}

/**
 * One turn in the thread, in whichever of the five states it is in.
 *
 * §7 names five and every one of them is real here, because the four that get
 * skipped are the four that decide whether an operator trusts the console:
 *
 * | state | what makes it different |
 * | --- | --- |
 * | **streaming** | the only message with no fixed end. Rendered *outside* the log region while it arrives — see `chat-thread.tsx` — so a screen reader is told once that a reply is coming rather than once per token. |
 * | **tool call** | a record rather than a spinner: the endpoint, its arguments and what came back, with a status badge carrying hue *and* a mark. |
 * | **proposal** | the only message with a decision on it. Two controls that keep their words, and no path from rendering to acting. |
 * | **error** | the turn failed, and the console says which part of it did rather than apologising in general. It is the one message that announces itself as an alert. |
 * | **permission denied** | not a message kind at all — it is a proposal whose confirming control is refused, which is the point. The same shape, in the same place, with the reason on it. |
 *
 * The author line names who spoke and when, in the data voice, because both are
 * values in a log. There are no avatars and no bubbles: a bubble is a card, and
 * a data surface here is bounded by a hairline and takes the corner its size
 * deserves.
 */
export function ChatMessage({ message, onDecide, busy }: ChatMessageProps) {
  const mine = message.kind === "person"

  return (
    <li
      className={styles.message}
      data-test="chat-message"
      data-kind={message.kind}
      data-streaming={message.streaming || undefined}
      data-message={message.id}
    >
      <div className={styles.byline}>
        <span className={styles.author}>{mine ? "you" : "comuki"}</span>
        <span className={styles.clock}>{message.at}</span>
      </div>

      <div className={styles.body}>
        {message.kind === "tool" && message.tool ? (
          <ToolCallCard tool={message.tool} />
        ) : null}

        {message.kind === "proposal" && message.proposal ? (
          <ProposalCard
            proposal={message.proposal}
            onDecide={onDecide}
            busy={busy}
          />
        ) : null}

        {message.kind === "error" ? (
          <p className={styles.error} data-test="chat-error" role="alert">
            <AlertTriangle className={styles.errorIcon} aria-hidden="true" />
            <span>{message.text}</span>
          </p>
        ) : null}

        {(message.kind === "person" || message.kind === "reply") &&
        message.text ? (
          <MessageText text={message.text} />
        ) : null}

        {message.handoff ? <ChatHandoffs query={message.handoff} /> : null}
      </div>
    </li>
  )
}
