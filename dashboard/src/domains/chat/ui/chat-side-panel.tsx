import { proposalCheck } from "@/domains/chat/model/proposals"
import type { ChatMessage, SlashCommand } from "@/domains/chat/model/types"
import { projectOf, useSession } from "@/shared/session"

import styles from "./chat-side-panel.module.css"

export interface ChatSidePanelProps {
  messages: ChatMessage[]
  commands: SlashCommand[]
}

/**
 * The optional third column: what is currently being decided, and what can be
 * typed.
 *
 * Two readings and no controls, which is the rule that keeps it from becoming
 * a second thread. The decision itself is made on the proposal card in the
 * conversation, where the question was asked — a confirm control up here would
 * be the same act in two places, and the two would eventually disagree about
 * which one was refused.
 *
 * The panel is the first thing to go when the board narrows: it is a
 * convenience, and the thread is the screen.
 */
export function ChatSidePanel({ messages, commands }: ChatSidePanelProps) {
  const session = useSession()

  // The newest undecided proposal — the one the conversation is standing on.
  const open = [...messages]
    .reverse()
    .map((message) => message.proposal)
    .find((proposal) => proposal && proposal.decision === undefined)

  const check = open ? proposalCheck(session, open) : null
  const project = open ? projectOf(session, open.projectId) : null

  return (
    <aside className={styles.panel} aria-label="Console side panel">
      <section className={styles.section}>
        <h2 className={styles.head}>awaiting a decision</h2>
        {open ? (
          <div className={styles.reading} data-test="chat-panel-proposal">
            <p className={styles.readingSummary}>{open.summary}</p>
            <p className={styles.readingMeta}>
              <span className={styles.value}>
                {project ? project.key : "no project"}
              </span>
              {open.steps ? (
                <span className={styles.value}>{open.steps.length} steps</span>
              ) : null}
            </p>
            {check?.denial ? (
              <p className={styles.denial}>{check.denial}</p>
            ) : null}
          </div>
        ) : (
          <p className={styles.quiet}>nothing is waiting on you here</p>
        )}
      </section>

      <section className={styles.section}>
        <h2 className={styles.head}>slash help</h2>
        <dl className={styles.help} data-test="chat-panel-help">
          {commands.map((command) => (
            <div key={command.name} className={styles.helpRow}>
              <dt className={styles.helpName}>{command.name}</dt>
              <dd className={styles.helpDescription}>{command.description}</dd>
            </div>
          ))}
        </dl>
      </section>
    </aside>
  )
}
