import { spendAxis, spendShare } from "@/domains/cost/model/cost"
import type { CostByApp } from "@/domains/cost/model/types"
import { RankedTable, type RankedRow } from "@/domains/cost/ui/ranked-table"

export interface SpendByAppProps {
  rows: CostByApp[]
  className?: string
}

/** One column: the figure the ranking is by. */
const COLUMNS = [{ label: "spend", strong: true }] as const

/**
 * Where the day's money went, ranked.
 *
 * The report's one ranking construction — a name, its length on the axis every
 * row shares, and the figure — because this is the same question the per-model
 * and per-project blocks ask: which one is expensive and by how much against
 * the one above it. Every row states its own figure, so the bars are drawn on
 * top of a reading rather than being one; the list is complete in words with
 * every channel removed.
 *
 * The axis is the largest spend in the breakdown, shared by every row. Bars on
 * their own scales cannot be compared, and comparing them is the whole task.
 */
export function SpendByApp({ rows, className }: SpendByAppProps) {
  const axis = spendAxis(rows)

  const ranked: RankedRow[] = rows.map((row) => ({
    id: row.app,
    label: <span title={row.app}>{row.app}</span>,
    share: spendShare(row, axis),
    figures: [
      /* One decimal, which is this screen's third precision and each one is a
         decision: cents for a per-success price, whole dollars for a day's
         total, a dime for a per-app share. */
      { value: `$${row.spend.toFixed(1)}`, "data-test": "spend-by-app-spend" },
    ],
  }))

  return (
    <RankedTable
      label="app"
      columns={COLUMNS}
      rows={ranked}
      empty="nothing spent today"
      data-test="spend-by-app"
      rowTest="spend-by-app-row"
      emptyTest="spend-empty"
      className={className}
    />
  )
}
