import { useMemo, useState } from "react"

import type { AutonomyRow } from "@/domains/settings/model/types"
import {
  DataTable,
  DataTableToolbar,
  Section,
  applyDataFilters,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import { createAutonomyColumns, getAutonomyId } from "./autonomy-columns"
import styles from "./settings-panel.module.css"
import tableStyles from "./settings-table.module.css"

export interface AutonomyPanelProps {
  rows: AutonomyRow[]
}

/**
 * Which classes of change the swarm carries out alone, and which ones stop for
 * a person.
 *
 * KNOWN GAP, recorded rather than papered over: this panel reads and does not
 * write, and unlike Apps, Rules and Keys that is *not* because its source is
 * the client's git. §12.1 of the FE requirements lists "plan approve on/off" as
 * a live setting — one the control plane reloads without a commit — so the row
 * that ought to carry a switch carries a badge instead. The panel deliberately
 * does not claim to be read-only-by-design: no "lives in git" line is written
 * here, because that would be a lie about where the setting comes from. The
 * missing control is a feature this screen has not been given yet.
 */
export function AutonomyPanel({ rows }: AutonomyPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(() => createAutonomyColumns(), [])
  const shown = useMemo(
    () => applyDataFilters(rows, filters, columns),
    [rows, filters, columns]
  )

  return (
    <Section
      variant="screen"
      data-test="settings-autonomy"
      title="Autonomy"
      note="what's auto · what needs a human"
    >
      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={setFilters}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          trailing={
            <span className={tableStyles.count} data-test="autonomy-count">
              {shown.length} shown
            </span>
          }
        />
      </div>
      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={shown}
          getRowId={getAutonomyId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={
            hasActiveFilters(filters)
              ? "no change classes match the current filters"
              : "no change class is classified"
          }
        />
      </div>
    </Section>
  )
}
