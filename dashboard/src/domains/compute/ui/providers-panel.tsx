import { useMemo, useState } from "react"

import type {
  ComputePool,
  ComputeProvider,
} from "@/domains/compute/model/types"
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

import { createProviderColumns, getProviderId } from "./provider-columns"
import styles from "./compute-panel.module.css"
import tableStyles from "./compute-table.module.css"

export interface ProvidersPanelProps {
  providers: ComputeProvider[]
  pools: ComputePool[]
  /** The provider a switch is in flight for. */
  switchingId: string | null
  onTakeWork: (provider: ComputeProvider) => void
}

/**
 * The provider registry: the two `IComputeProvider` implementations v1 has, the
 * endpoints behind them, and which one is taking new starts.
 *
 * It holds its own filter state rather than taking it from the page, for the
 * same reason the queue's two halves do: the registry and the rollout table
 * below are narrowed by different questions — "what is this docker host doing"
 * and "what is still on the old profiles ref" — and one shared filter would
 * make the screen a single table with a gap in the middle.
 */
export function ProvidersPanel({
  providers,
  pools,
  switchingId,
  onTakeWork,
}: ProvidersPanelProps) {
  const session = useSession()
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(
    () =>
      createProviderColumns({
        pools,
        switchingId,
        onTakeWork,
        session,
      }),
    [pools, switchingId, onTakeWork, session]
  )

  const rows = useMemo(
    () => applyDataFilters(providers, filters, columns),
    [providers, filters, columns]
  )

  const emptyLabel = hasActiveFilters(filters)
    ? "no providers match the current filters"
    : "no compute provider is registered"

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
            <span className={tableStyles.count} data-test="providers-count">
              {rows.length} shown
            </span>
          }
        />
      </div>

      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getProviderId}
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
