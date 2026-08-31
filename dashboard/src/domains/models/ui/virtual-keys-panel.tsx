import { useMemo, useState } from "react"

import { expiredKeys, keyOrder, keysNearCap } from "@/domains/models/model/keys"
import type { ModelEndpoint, VirtualKey } from "@/domains/models/model/types"
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

import { createKeyColumns, getKeyId } from "./key-columns"
import styles from "./models-panel.module.css"
import tableStyles from "./models-table.module.css"

export interface VirtualKeysPanelProps {
  keys: VirtualKey[]
  endpoints: ModelEndpoint[]
  /** Whether the caps in this table are actually being applied right now. */
  enforced: boolean
  revokingId: string | null
  onRevoke: (entry: VirtualKey) => void
}

/**
 * The keys, worst first.
 *
 * "Worst" is not fullest: a key that already stopped working comes before one
 * that is about to, because a run failing on an expired key is happening now
 * and a cap is a decision somebody still has time to make. Revoked keys sort
 * last — they are history, kept so the registry can answer what happened to the
 * key that used to be here.
 *
 * That order is the order rows arrive in even once the head is sortable: the
 * table sorts what it is given and breaks ties on the incoming index, so an
 * explicit sort is the primary key and this stays the tiebreak beneath it.
 */
export function VirtualKeysPanel({
  keys,
  endpoints,
  enforced,
  revokingId,
  onRevoke,
}: VirtualKeysPanelProps) {
  const session = useSession()
  const [filters, setFilters] = useState<DataTableFilterValues>({})
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(
    () =>
      createKeyColumns({
        endpoints,
        enforced,
        revokingId,
        onRevoke,
        session,
      }),
    [endpoints, enforced, revokingId, onRevoke, session]
  )

  const rows = useMemo(
    () => keyOrder(applyDataFilters(keys, filters, columns)),
    [keys, filters, columns]
  )

  const nearCap = useMemo(() => keysNearCap(rows).length, [rows])
  const expired = useMemo(() => expiredKeys(rows).length, [rows])

  const emptyLabel = hasActiveFilters(filters)
    ? "no keys match the current filters"
    : "no virtual key has been issued"

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
            <span className={tableStyles.count} data-test="keys-count">
              {expired > 0 ? `${expired} expired · ` : ""}
              {nearCap > 0 ? `${nearCap} near the cap · ` : ""}
              {rows.length} shown
            </span>
          }
        />
      </div>

      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getKeyId}
          density="comfortable"
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
