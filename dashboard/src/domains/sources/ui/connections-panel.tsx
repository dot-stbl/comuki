import { useMemo, useState } from "react"

import type {
  SourceConnection,
  SourceState,
} from "@/domains/sources/model/types"
import { getConnectionId } from "@/domains/sources/ui/sources-columns"
import {
  DataTable,
  DataTableToolbar,
  applyDataFilters,
  hasActiveFilters,
  type DataColumn,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import styles from "./connections-panel.module.css"

export interface ConnectionsPanelProps {
  /** Built by the screen, because the acts on a row are the screen's. */
  columns: DataColumn<SourceConnection>[]
  connections: SourceConnection[]
  /**
   * A string to narrow to on arrival — see `SourcesPage`'s `focus`.
   *
   * Seeded into the toolbar's own promoted filter and then owned by the
   * toolbar, so the narrowing is *visible* in the control that did it and one
   * click from being cleared. A list that arrived short for a reason the
   * operator cannot see in the chrome is the coupling this product's tables
   * are not allowed to have.
   */
  initialFilter?: string
}

/** Broken first, then off, then working. Triage, not the alphabet. */
const STATE_RANK: Record<SourceState, number> = {
  error: 0,
  disabled: 1,
  connected: 2,
}

/**
 * The connections list and the bar above it.
 *
 * It owns the table's own view state — filters, visibility, sorting, widths —
 * because all four are things this panel's user does to this panel's table, and
 * none of them are the screen's business. The columns come in from the screen,
 * because the acts on a row are: a cell that offers to disconnect a source has
 * to ask the row's project whether this person may.
 *
 * Rows arrive worst-first and stay that way until somebody sorts the head. The
 * two compose rather than compete: the table sorts what it is given and breaks
 * ties on the incoming index, so an explicit sort is the primary key and triage
 * is the tiebreak beneath it. Clear the sort and the screen is back to opening
 * on the connection somebody came here about.
 */
export function ConnectionsPanel({
  columns,
  connections,
  initialFilter,
}: ConnectionsPanelProps) {
  // `name` is the promoted text filter's key — the column the toolbar lifts
  // into its search field — so seeding it is the same act as typing in the box.
  const [filters, setFilters] = useState<DataTableFilterValues>(() => {
    const seeded: DataTableFilterValues = {}
    if (initialFilter) {
      seeded.name = initialFilter
    }
    return seeded
  })
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const rows = useMemo(() => {
    const filtered = applyDataFilters(connections, filters, columns)
    return [...filtered].sort(
      (a, b) => STATE_RANK[a.state] - STATE_RANK[b.state]
    )
  }, [connections, filters, columns])

  const emptyLabel = hasActiveFilters(filters)
    ? "No connections match the current filters."
    : "No sources are connected."

  return (
    <div className={styles.panel} data-test="connections-panel">
      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={setFilters}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          trailing={<span className={styles.count}>{rows.length} shown</span>}
        />
      </div>

      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getConnectionId}
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
    </div>
  )
}
