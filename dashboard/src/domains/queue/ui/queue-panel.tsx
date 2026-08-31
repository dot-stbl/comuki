import { useCallback, useMemo, useState } from "react"

import type { ProjectRef } from "@/shared/session"
import {
  DataTable,
  DataTableToolbar,
  applyDataFilters,
  dataFilterSpecs,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import { queueOrder, unclaimedOver, AGE_STALLED_SEC } from "@/domains/queue/model/queue"
import type { QueueItem } from "@/domains/queue/model/types"

import { createQueueColumns, getQueueItemId } from "./queue-columns"
import tableStyles from "./queue-table.module.css"
import styles from "./queue-panel.module.css"

export interface QueuePanelProps {
  items: QueueItem[]
  projects: ProjectRef[]
  /**
   * The promoted search filter, held in the URL by the route as `?q=`.
   *
   * Controlled-or-not, the way the kit's own table takes `columnVisibility`.
   * The route hands both halves over; a story or a test that hands neither
   * gets a panel that keeps the value itself and behaves exactly as before.
   */
  search?: string
  onSearchChange?: (next: string) => void
}

/**
 * The queue half: every work item the orchestrator has put out for claim.
 *
 * It holds its own filters rather than taking them from the page, because the
 * two halves of this screen are narrowed independently on purpose — an
 * operator asks "what is queued on `verifier`" and "what workers run
 * `verifier`" as two questions, and answering them with one filter would make
 * the screen a single table with a gap in the middle.
 */
export function QueuePanel({
  items,
  projects,
  search,
  onSearchChange,
}: QueuePanelProps) {
  // Every filter except the promoted search, which lives in the URL when the
  // route is driving.
  const [ownFilters, setOwnFilters] = useState<DataTableFilterValues>({})
  const [localSearch, setLocalSearch] = useState("")
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(() => createQueueColumns({ projects }), [projects])

  /* The filter the toolbar promotes to its search field, asked of the same
     declarations the toolbar reads — see the derivation rule on
     `DataTableToolbar`. Asking rather than naming it is what keeps the URL's
     value and the box it lands in from ever coming apart. */
  const searchId = useMemo(
    () =>
      dataFilterSpecs(columns).find((spec) => spec.filter.kind === "text")?.id,
    [columns]
  )

  const searchValue = onSearchChange ? (search ?? "") : localSearch
  const setSearchValue = onSearchChange ?? setLocalSearch

  const filters = useMemo(
    () => (searchId ? { ...ownFilters, [searchId]: searchValue } : ownFilters),
    [ownFilters, searchId, searchValue]
  )

  const onFiltersChange = useCallback(
    (next: DataTableFilterValues) => {
      if (!searchId) {
        setOwnFilters(next)
        return
      }
      const { [searchId]: text = "", ...rest } = next
      setOwnFilters(rest)
      setSearchValue(text)
    },
    [searchId, setSearchValue]
  )

  // Worst first, minus whatever the toolbar is filtering out. Triage stays the
  // order rows arrive in even once the head is sortable: the table sorts what
  // it is given and breaks ties on the incoming index, so an explicit sort is
  // the primary key and this is the tiebreak beneath it.
  const rows = useMemo(
    () => queueOrder(applyDataFilters(items, filters, columns)),
    [items, filters, columns]
  )

  const stalled = useMemo(
    () => unclaimedOver(rows, AGE_STALLED_SEC),
    [rows]
  )

  const emptyLabel = filters.profile
    ? "nothing queued on this profile"
    : hasActiveFilters(filters)
      ? "no work items match the current filters"
      : "the queue is empty"

  return (
    <>
      <div className={styles.toolbar}>
        <DataTableToolbar
          columns={columns}
          filters={filters}
          onFiltersChange={onFiltersChange}
          columnVisibility={columnVisibility}
          onColumnVisibilityChange={setColumnVisibility}
          trailing={
            <span className={tableStyles.count} data-test="queue-count">
              {stalled > 0 ? `${stalled} waiting too long · ` : ""}
              {rows.length} shown
            </span>
          }
        />
      </div>

      <div className={styles.tableArea}>
        <DataTable
          columns={columns}
          data={rows}
          getRowId={getQueueItemId}
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
