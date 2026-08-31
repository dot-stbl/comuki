import { useMemo, useState } from "react"

import type { EvalCase } from "@/domains/knowledge/model/types"
import { DataTable, type DataTableSorting } from "@/shared/ui"

import { createEvalColumns, getEvalId } from "./eval-columns"
import styles from "./knowledge-panel.module.css"

export interface EvalHarnessTableProps {
  cases: EvalCase[]
}

/**
 * What the current rule set does to the golden tasks.
 *
 * Four columns and a handful of rows, so it carries no toolbar: a filter over
 * four rows is chrome that narrows nothing, and the one ordering that matters —
 * regressions first — is a click on the `delta` head rather than a control the
 * screen has to render. It sizes itself to its rows and caps at
 * `--h-table-body`, so a harness that grows to fifty tasks scrolls inside its
 * own frame instead of pushing the rest of the screen off the page.
 */
export function EvalHarnessTable({ cases }: EvalHarnessTableProps) {
  const columns = useMemo(() => createEvalColumns(), [])
  const [sorting, setSorting] = useState<DataTableSorting>([])

  return (
    <div className={styles.tableArea}>
      <DataTable
        columns={columns}
        data={cases}
        getRowId={getEvalId}
        density="compact"
        sorting={sorting}
        onSortingChange={setSorting}
        emptyLabel="no golden task has been run against this revision"
      />
    </div>
  )
}
