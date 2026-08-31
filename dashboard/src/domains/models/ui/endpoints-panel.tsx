import { useMemo, useState } from "react"

import type { ModelEndpoint } from "@/domains/models/model/types"
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

import { createEndpointColumns, getEndpointId } from "./endpoint-columns"
import styles from "./models-panel.module.css"
import tableStyles from "./models-table.module.css"

export interface EndpointsPanelProps {
  endpoints: ModelEndpoint[]
}

/**
 * The upstreams, on the two wires v1 speaks.
 *
 * Holds its own filter state rather than taking it from the page: this table
 * and the key table below are narrowed by different questions — "what is
 * answering" and "what can this key reach" — and one shared filter would make
 * the screen a single table with gaps in the middle.
 */
export function EndpointsPanel({ endpoints }: EndpointsPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(() => createEndpointColumns(), [])

  const rows = useMemo(
    () => applyDataFilters(endpoints, filters, columns),
    [endpoints, filters, columns]
  )

  const emptyLabel = hasActiveFilters(filters)
    ? "no endpoints match the current filters"
    : "no upstream endpoint is configured"

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
            <span className={tableStyles.count} data-test="endpoints-count">
              {rows.length} shown
            </span>
          }
        />
      </div>

      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getEndpointId}
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
