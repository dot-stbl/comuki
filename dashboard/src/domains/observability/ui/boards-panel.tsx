import { ExternalLink, MinusCircle } from "lucide-react"

import type { Board } from "@/domains/observability/model/types"
import { Tooltip, buttonClass } from "@/shared/ui"

import styles from "./boards-panel.module.css"

export interface BoardsPanelProps {
  boards: Board[]
}

/**
 * The boards, as links.
 *
 * Every entry is an anchor, never an embed, and never a preview that would
 * imply one is coming. The reason is on the page beside this list: infra logs
 * and run timelines are read on different clocks by people asking different
 * questions, and a surface that showed both would teach an operator to look for
 * a run's story inside a metrics board and then find half of it.
 *
 * `buttonClass()` rather than a `Button` wrapping an anchor: a link that must
 * look like a control takes the classes, because nesting an anchor inside a
 * button breaks keyboard and assistive traversal in both directions.
 *
 * A board with no url is not an error and does not read as one. Its definition
 * is in our repository and nobody has imported it into this Grafana yet, which
 * is a next step rather than a failure — so it gets the waiting hue, a distinct
 * mark, and the sentence that says what to do sits under the list.
 */
export function BoardsPanel({ boards }: BoardsPanelProps) {
  return (
    <div className={styles.boards} data-test="boards-panel">
      {boards.map((board) => (
        <div key={board.kind} className={styles.board} data-test="board">
          <div className={styles.text}>
            <h3 className={styles.title}>{board.title}</h3>
            <p className={styles.summary}>{board.summary}</p>
            <p className={styles.meta}>
              uid {board.uid} · definition updated {board.updatedAt}
            </p>
          </div>
          <div className={styles.action}>
            {board.url ? (
              // Two words, so the glyph carries the act. The board's own
              // title is a heading two lines up, so the name says which one
              // is about to open rather than repeating "board".
              <Tooltip content={`Open ${board.title}`}>
                <a
                  className={buttonClass({
                    variant: "outline",
                    size: "icon-sm",
                  })}
                  href={board.url}
                  target="_blank"
                  rel="noreferrer"
                  aria-label={`Open ${board.title}`}
                  data-test="board-link"
                >
                  <ExternalLink aria-hidden="true" />
                </a>
              </Tooltip>
            ) : (
              <span className={styles.pending} data-test="board-not-imported">
                <MinusCircle className={styles.pendingIcon} aria-hidden="true" />
                not imported yet
              </span>
            )}
          </div>
        </div>
      ))}
    </div>
  )
}
