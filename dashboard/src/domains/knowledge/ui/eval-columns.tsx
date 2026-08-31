import type { EvalCase } from "@/domains/knowledge/model/types"
import { StatusBadge, rankSort, type DataColumn } from "@/shared/ui"

import { EvalDeltaMark } from "./knowledge-badges"
import styles from "./knowledge-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getEvalId = (item: EvalCase) => item.task

/**
 * Regressions first, then improvements, then the tasks nothing happened to.
 * Alphabetically `+`, `-` and `=` are no order at all, and the row somebody
 * came to this screen about is the one that got worse.
 */
const deltaSort = rankSort({ "-": 0, "+": 1, "=": 2 })

/**
 * The golden-task harness, as columns.
 *
 * A rule edit is a change to what every worker is told, and the only honest way
 * to know what it did is to run the same tasks before and after. So the row is
 * exactly that sentence: which task, what it did before, what it does now, and
 * the difference stated a third time in words — because reading two badges and
 * subtracting them is work the screen should have done.
 *
 * No factory argument and no session: nothing on this row is an act. The moment
 * one is, this takes the session the way `createUserColumns` does — `cell` is
 * called as a plain function while the table builds a row, so a hook inside one
 * throws.
 */
export function createEvalColumns(): DataColumn<EvalCase>[] {
  return [
    {
      accessorKey: "task",
      header: "task",
      cell: ({ row }) => (
        <span className={styles.task} title={row.original.task}>
          {row.original.task}
        </span>
      ),
      meta: { width: 220, pinned: true },
    },
    {
      accessorKey: "before",
      header: "before",
      cell: ({ row }) => (
        <StatusBadge
          status={row.original.before === "pass" ? "success" : "failed"}
          size="sm"
        >
          {row.original.before}
        </StatusBadge>
      ),
      meta: { width: 104 },
    },
    {
      accessorKey: "after",
      header: "after",
      cell: ({ row }) => (
        <StatusBadge
          status={row.original.after === "pass" ? "success" : "failed"}
          size="sm"
        >
          {row.original.after}
        </StatusBadge>
      ),
      meta: { width: 104 },
    },
    {
      accessorKey: "delta",
      header: "delta",
      sortFn: deltaSort,
      cell: ({ row }) => <EvalDeltaMark delta={row.original.delta} />,
      meta: { width: 132, align: "end", label: "delta" },
    },
  ]
}
