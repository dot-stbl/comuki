import { useMemo, useState } from "react"

import type { ProviderKey } from "@/domains/settings/model/types"
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

import { createProviderKeyColumns, getProviderKeyId } from "./keys-columns"
import styles from "./settings-panel.module.css"
import tableStyles from "./settings-table.module.css"

export interface KeysPanelProps {
  keys: ProviderKey[]
}

/**
 * Which model providers the proxy can reach, and whether any of them is about
 * to stop answering.
 *
 * Read-only, and the note says why: the credentials come from env and the
 * rotation runs inside the proxy, so there is no value on this screen anybody
 * could edit and no secret on it anybody could read. What the panel is for is
 * the last column — a key at `budget 67%` is the reason a run will fail in
 * three hours, and it is legible here before it is legible anywhere else.
 */
export function KeysPanel({ keys }: KeysPanelProps) {
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(() => createProviderKeyColumns(), [])
  const rows = useMemo(
    () => applyDataFilters(keys, filters, columns),
    [keys, filters, columns]
  )

  return (
    <Section
      variant="screen"
      data-test="settings-keys"
      title="Provider keys"
      note="read-only · keys come from env, rotation runs in the proxy"
    >
      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={setFilters}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          trailing={
            <span className={tableStyles.count} data-test="keys-count">
              {rows.length} shown
            </span>
          }
        />
      </div>
      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getProviderKeyId}
          density="compact"
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          sorting={sorting}
          onSortingChange={setSorting}
          columnSizing={columnSizing}
          onColumnSizingChange={setColumnSizing}
          emptyLabel={
            hasActiveFilters(filters)
              ? "no keys match the current filters"
              : "the proxy holds no provider key"
          }
        />
      </div>
    </Section>
  )
}
