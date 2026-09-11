import type { ToolPart } from "@/domains/chat/model/types"
import { StatusBadge } from "@/shared/ui"

import styles from "./chat-message.module.css"

export interface ToolCallCardProps {
  tool: ToolPart
}

/**
 * One call the assistant made against the Orchestration API, shown rather than
 * summarised.
 *
 * The console's tools are the product's own endpoints, and an operator who
 * cannot see which one was called and what it answered has no way to tell a
 * wrong answer from a broken one. So the call is a *record*, not a spinner: the
 * endpoint, the arguments it went out with, what came back, and — the state
 * most consoles skip — what it said when it failed.
 *
 * Every part of it is a value, so every part of it is in the data voice. The
 * status is carried by a badge rather than by the border alone, because the
 * border is one channel and a status in this product always has two.
 *
 * It takes the **part** shape rather than the flat `ToolCall`: the seed's
 * `args`/`result` and the wire's `inputJson`/`outputJson` are the same fact,
 * and `model/parts.ts` converts one into the other so this card renders one
 * type and cannot grow a branch only one path reaches.
 */
export function ToolCallCard({ tool }: ToolCallCardProps) {
  return (
    <div
      className={styles.tool}
      data-test="chat-tool"
      data-status={tool.status}
    >
      <div className={styles.toolHead}>
        <span className={styles.toolName}>{tool.name}</span>
        <span className={styles.toolArgs}>{tool.inputJson}</span>
        {tool.durationMs === undefined ? null : (
          <span className={styles.toolClock} data-test="chat-tool-duration">
            {formatDuration(tool.durationMs)}
          </span>
        )}
        <StatusBadge status={tool.status} size="sm" />
      </div>
      {tool.outputJson ? (
        <p className={styles.toolResult} data-test="chat-tool-result">
          {tool.outputJson}
        </p>
      ) : null}
    </div>
  )
}

/**
 * How long the call took, in the unit the number actually deserves.
 *
 * Milliseconds up to a second, because that is the range where the difference
 * between 40 and 400 matters; seconds above it, because nobody reads
 * `31420ms`. One decimal and no more — a call that took half a minute is not
 * measured to the millisecond by anything the operator can act on.
 */
function formatDuration(ms: number): string {
  return ms < 1000 ? `${Math.round(ms)}ms` : `${(ms / 1000).toFixed(1)}s`
}
