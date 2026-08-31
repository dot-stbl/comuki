import { useMemo, useState } from "react"

import type { AppRegistryItem } from "@/domains/settings/model/types"
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

import { createAppColumns, getAppId, uniqueDeployTargets } from "./apps-columns"
import styles from "./settings-panel.module.css"
import tableStyles from "./settings-table.module.css"

export interface AppsPanelProps {
  apps: AppRegistryItem[]
}

/**
 * What the swarm is allowed to build, and where each of those things lives.
 *
 * Read-only, and the note under the title says why rather than leaving the
 * operator to discover it by finding no controls: the registry is declared in
 * the client's own repository, so changing it is a commit over there and not a
 * form over here. A panel that stayed silent about that would read as a screen
 * somebody forgot to finish.
 */
export function AppsPanel({ apps }: AppsPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const deployTargets = useMemo(() => uniqueDeployTargets(apps), [apps])
  const columns = useMemo(
    () => createAppColumns(deployTargets),
    [deployTargets]
  )
  const rows = useMemo(
    () => applyDataFilters(apps, filters, columns),
    [apps, filters, columns]
  )

  return (
    <Section
      variant="screen"
      data-test="settings-apps"
      title="Apps"
      note="read-only · the registry is declared in the client's git"
    >
      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={setFilters}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          trailing={
            <span className={tableStyles.count} data-test="apps-count">
              {rows.length} shown
            </span>
          }
        />
      </div>
      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getAppId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={
            hasActiveFilters(filters)
              ? "no apps match the current filters"
              : "the registry declares no app"
          }
        />
      </div>
    </Section>
  )
}
