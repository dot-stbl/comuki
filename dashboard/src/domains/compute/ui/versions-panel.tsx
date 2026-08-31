import { useMemo, useState } from "react"

import { strandedIdle, targetVersion } from "@/domains/compute/model/capacity"
import type { WorkerVersion } from "@/domains/compute/model/types"
import { useSession } from "@/shared/session"
import {
  DataTable,
  DataTableToolbar,
  applyDataFilters,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import { createVersionColumns, getVersionId } from "./version-columns"
import styles from "./compute-panel.module.css"
import tableStyles from "./compute-table.module.css"

export interface VersionsPanelProps {
  versions: WorkerVersion[]
  /** The label a teardown is in flight for, as `digest|ref`. */
  retiringLabel: string | null
  onRetire: (version: WorkerVersion) => void
}

/**
 * What the pool is actually running, by label.
 *
 * Rows arrive target-first and then by how many containers are stranded on
 * them, because that is the order the question is asked in: what are we
 * starting today, and what is sitting there that will never be handed an item.
 */
export function VersionsPanel({
  versions,
  retiringLabel,
  onRetire,
}: VersionsPanelProps) {
  const session = useSession()
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const target = useMemo(() => targetVersion(versions), [versions])

  const columns = useMemo(
    () =>
      createVersionColumns({
        target,
        retiringLabel,
        onRetire,
        session,
      }),
    [target, retiringLabel, onRetire, session]
  )

  // Target first, then most-stranded first. This is the order rows arrive in
  // even once the head is sortable: the table sorts what it is given and breaks
  // ties on the incoming index, so an explicit sort is the primary key and this
  // stays the tiebreak beneath it.
  const rows = useMemo(() => {
    const filtered = applyDataFilters(versions, filters, columns)
    return [...filtered].sort((a, b) => {
      if (a.target !== b.target) {
        return a.target ? -1 : 1
      }
      return b.idle - a.idle
    })
  }, [versions, filters, columns])

  const stranded = useMemo(() => strandedIdle(rows), [rows])

  const emptyLabel = hasActiveFilters(filters)
    ? "no labels match the current filters"
    : "no workers are up"

  return (
    <>
      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={setFilters}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          trailing={
            <span className={tableStyles.count} data-test="versions-count">
              {stranded > 0 ? `${stranded} idle never matched · ` : ""}
              {rows.length} shown
            </span>
          }
        />
      </div>

      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getVersionId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={emptyLabel}
        />
      </div>
    </>
  )
}
