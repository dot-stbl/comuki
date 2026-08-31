import { useMemo } from "react"
import { Link } from "@tanstack/react-router"
import { ArrowUpRight } from "lucide-react"

import { useSearchCatalogue } from "@/app/search"
import { chatHandoffs } from "@/domains/chat/model/references"
import { useSession } from "@/shared/session"

import styles from "./chat-message.module.css"

export interface ChatHandoffsProps {
  /** What the assistant was asked to find. */
  query: string
}

/**
 * The console's answer to "show me a list": a filter on the real screen.
 *
 * This is the third settled decision, made visible. A chat that renders its own
 * runs table is a second duty screen, and a second duty screen drifts from the
 * first — different columns, different filters, a different idea of what
 * "waiting" means. So the console hands the question over instead, to the
 * screen that already knows how to answer it, with the filter already applied.
 *
 * The destinations are not written here. They come from `resolveQuery`, the
 * same call the command palette makes, so the console and the palette cannot
 * disagree about where a hand-off lands or what parameter it lands with.
 */
export function ChatHandoffs({ query }: ChatHandoffsProps) {
  const session = useSession()
  const catalogue = useSearchCatalogue()

  const handoffs = useMemo(
    () => chatHandoffs(query, session, catalogue),
    [query, session, catalogue]
  )

  if (handoffs.length === 0) {
    return null
  }

  return (
    <ul className={styles.handoffs} data-test="chat-handoffs">
      {handoffs.map((handoff) => (
        <li key={handoff.id}>
          <Link
            to={handoff.href}
            className={styles.handoff}
            data-test="chat-handoff"
          >
            <ArrowUpRight className={styles.handoffIcon} aria-hidden="true" />
            {/* Two voices in one line, doing the work they are for: the
                sentence is meaning and the query is a value. */}
            <span className={styles.handoffWords}>
              search {handoff.where} for
            </span>
            <span className={styles.handoffQuery}>«{handoff.query}»</span>
          </Link>
        </li>
      ))}
    </ul>
  )
}
