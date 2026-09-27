import { useTranslation } from "react-i18next"

import { modelAxis, modelShare } from "@/domains/cost/model/cost"
import type { CostByModel } from "@/domains/cost/model/cost"
import { RankedTable, type RankedRow } from "@/domains/cost/ui/ranked-table"

export interface SpendByModelProps {
  rows: CostByModel[]
  className?: string
}

/**
 * What the swarm spent on each model, ranked.
 *
 * Same construction as the per-app and per-project breakdowns, because the
 * comparison is the same comparison: which model is expensive and by how much
 * against the one above it. The rows state their own spend and their own
 * tokens, so the bar is drawn on top of a reading rather than being one.
 *
 * Two figure columns, and the reason this block is a table rather than a list:
 * tokens and spend are both numbers, both at the end of their column, both
 * tabular — and nothing but a head says which is the volume and which is the
 * money. A `<ul>` whose `<li>` is a grid of fixed tracks is a table that has
 * given up its column names, and this is the block where giving them back
 * fixes a real reading rather than only the markup.
 */
export function SpendByModel({ rows, className }: SpendByModelProps) {
  const { t } = useTranslation("cost")
  const axis = modelAxis(rows)

  const ranked: RankedRow[] = rows.map((row) => ({
    id: row.model,
    label: <span title={row.model}>{row.model}</span>,
    share: modelShare(row, axis),
    figures: [
      {
        value: formatTokens(row.tokens),
        /* The run count is the reading the column truncates; it rides in the
           cell's `title`, because the token volume is the second axis of the
           model question ("where is the money *and* where is the work"). */
        title: t("byModel.tokensTitle", {
          tokens: row.tokens.toLocaleString("en-US"),
          runs: row.runs,
        }),
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
      label={t("table.model")}
      columns={[
        { label: t("column.tokens"), help: true },
        { label: t("column.spend"), strong: true },
      ]}
      rows={ranked}
      empty={t("byModel.empty")}
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
