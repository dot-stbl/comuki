import { useMemo, useState } from "react"
import { useTranslation } from "react-i18next"

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
  const { t } = useTranslation("models")
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(
    () => createRoutingColumns({ endpoints, t }),
    [endpoints, t]
  )

  const rows = useMemo(
    () => applyDataFilters(routes, filters, columns),
    [routes, filters, columns]
  )

  const emptyLabel = hasActiveFilters(filters)
    ? t("routing.emptyFiltered")
    : t("routing.empty")

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
              {t("routing.count", { count: rows.length })}
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
