import { useMemo, useState } from "react"

import type { ModelEndpoint, ModelRoute } from "@/domains/models/model/types"
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

import { createRoutingColumns, getRouteId } from "./routing-columns"
import styles from "./models-panel.module.css"
import tableStyles from "./models-table.module.css"

export interface RoleRoutingPanelProps {
  routes: ModelRoute[]
  endpoints: ModelEndpoint[]
}

/**
 * Role → model, the platform's own resolution.
 *
 * Not the project's routing panel under Settings, which is a form a
 * project-admin fills in for their project. This is the platform table the
 * whole swarm resolves through — the same distinction the two tiers of the rail
 * are built on.
 */
export function RoleRoutingPanel({ routes, endpoints }: RoleRoutingPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(
    () => createRoutingColumns({ endpoints }),
    [endpoints]
  )

  const rows = useMemo(
    () => applyDataFilters(routes, filters, columns),
    [routes, filters, columns]
  )

  const emptyLabel = hasActiveFilters(filters)
    ? "no roles match the current filters"
    : "no role has been routed to a model"

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
            <span className={tableStyles.count} data-test="routes-count">
              {rows.length} shown
            </span>
          }
        />
      </div>

      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getRouteId}
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
