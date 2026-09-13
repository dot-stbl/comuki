import { modelAxis, modelShare } from "@/domains/cost/model/cost"
import type { CostByModel } from "@/domains/cost/model/cost"
import { cn } from "@/shared/lib/utils"

import styles from "./spend-by-model.module.css"

export interface SpendByModelProps {
  rows: CostByModel[]
  className?: string
}

/**
 * What the swarm spent on each model, ranked.
 *
 * Same shape as the per-app breakdown — a list, a shared axis, lengths, no
 * chart library — because the comparison is the same comparison: which model
 * is expensive and by how much against the one above it. The rows state
 * their own spend and their own tokens, so the bar is drawn on top of a
 * reading rather than being one.
 *
 * Tooltip: a row's title carries the run count on hover — the figure the
 * column truncates — because the token volume is the second axis of the
 * model question ("where is the money *and* where is the work"), and it
 * would otherwise need its own column.
 */
export function SpendByModel({ rows, className }: SpendByModelProps) {
  const axis = modelAxis(rows)

  if (rows.length === 0) {
    return (
      <p
        className={cn(styles.empty, className)}
        data-test="spend-by-model-empty"
      >
        no model spend this period
      </p>
    )
  }

  return (
    <ul className={cn(styles.rows, className)} data-test="spend-by-model">
      {rows.map((row) => (
        <li
          key={row.model}
          className={styles.row}
          data-test="spend-by-model-row"
          data-model={row.model}
        >
          <span className={styles.model} title={row.model}>
            {row.model}
          </span>
          <span className={styles.channel} aria-hidden="true">
            <span
              className={styles.fill}
              style={{
                inlineSize: `${Math.round(modelShare(row, axis) * 100)}%`,
              }}
            />
          </span>
          <span
            className={styles.tokens}
            title={`${row.tokens.toLocaleString("en-US")} tokens across ${row.runs} runs`}
            data-test="spend-by-model-tokens"
          >
            {formatTokens(row.tokens)}
          </span>
          <span className={styles.spend} data-test="spend-by-model-spend">
            ${row.spend.toFixed(2)}
          </span>
        </li>
      ))}
    </ul>
  )
}

/** Token count, in `k` or `M` — never a `,` separator. */
function formatTokens(tokens: number): string {
  if (tokens >= 1_000_000) {
    return `${(tokens / 1_000_000).toFixed(1)}M`
  }
  if (tokens >= 1_000) {
    return `${(tokens / 1_000).toFixed(1)}k`
  }
  return `${tokens}`
}
