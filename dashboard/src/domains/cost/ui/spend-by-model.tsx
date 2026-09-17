import { modelAxis, modelShare } from "@/domains/cost/model/cost"
import type { CostByModel } from "@/domains/cost/model/cost"
import { RankedTable, type RankedRow } from "@/domains/cost/ui/ranked-table"

export interface SpendByModelProps {
  rows: CostByModel[]
  className?: string
}

/**
 * Two figure columns, and the reason this block is a table rather than a list.
 *
 * Tokens and spend are both numbers, both at the end of their column, both
 * tabular — and nothing but a head says which is the volume and which is the
 * money. A `<ul>` whose `<li>` is a grid of fixed tracks is a table that has
 * given up its column names, and this is the block where giving them back
 * fixes a real reading rather than only the markup.
 */
const COLUMNS = [
  /* The run count is the reading the column truncates; it rides in the cell's
     `title`, because the token volume is the second axis of the model question
     ("where is the money *and* where is the work"). */
  { label: "tokens", help: true },
  { label: "spend", strong: true },
] as const

/**
 * What the swarm spent on each model, ranked.
 *
 * Same construction as the per-app and per-project breakdowns, because the
 * comparison is the same comparison: which model is expensive and by how much
 * against the one above it. The rows state their own spend and their own
 * tokens, so the bar is drawn on top of a reading rather than being one.
 */
export function SpendByModel({ rows, className }: SpendByModelProps) {
  const axis = modelAxis(rows)

  const ranked: RankedRow[] = rows.map((row) => ({
    id: row.model,
    label: <span title={row.model}>{row.model}</span>,
    share: modelShare(row, axis),
    figures: [
      {
        value: formatTokens(row.tokens),
        title: `${row.tokens.toLocaleString("en-US")} tokens across ${row.runs} runs`,
        "data-test": "spend-by-model-tokens",
      },
      {
        value: `$${row.spend.toFixed(2)}`,
        "data-test": "spend-by-model-spend",
      },
    ],
  }))

  return (
    <RankedTable
      label="model"
      columns={COLUMNS}
      rows={ranked}
      empty="no model spend this period"
      data-test="spend-by-model"
      rowTest="spend-by-model-row"
      emptyTest="spend-by-model-empty"
      className={className}
    />
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
