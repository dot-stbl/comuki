import { useCallback, useMemo, useState } from "react"

import { useSession, type ProjectRef } from "@/shared/session"
import {
  ConfirmDialog,
  DataTable,
  DataTableToolbar,
  applyDataFilters,
  dataFilterSpecs,
  emptyFilterValues,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import { useDrainWorker, useForceStopWorker } from "@/domains/queue/api/mutations"
import { minIdleFor, resolveWorkerEmpty } from "@/domains/queue/model/queue"
import type { QueueItem, Worker, WorkerPool } from "@/domains/queue/model/types"

import tableStyles from "./queue-table.module.css"
import { WorkerEmpty } from "./worker-empty"
import { createWorkerColumns, getWorkerId } from "./worker-columns"
import styles from "./workers-panel.module.css"

export interface WorkersPanelProps {
  workers: Worker[]
  /** The queue, so a busy worker can name its work and an empty pool can
   *  say whether anything is actually waiting on it. */
  items: QueueItem[]
  pools: WorkerPool[]
  projects: ProjectRef[]
  /**
   * The promoted search filter, held in the URL by the route as `?w=`.
   *
   * A parameter of its own rather than a share of the queue half's `q`,
   * because the two halves of this screen are narrowed independently on
   * purpose — and because they answer to different strings: a worker id, a
   * provider handle and an image digest on this side, a work item and a run on
   * the other. One value serving both would empty whichever half it was not
   * written for.
   */
  search?: string
  onSearchChange?: (next: string) => void
}

/**
 * The pool half: what is up, what each container is holding, and the two acts
 * a person can take on one.
 *
 * It reads the queue as well as the pool, and that is the point of putting the
 * two halves on one screen: "no workers" only means something once you know
 * whether anything is queued, and a queued item only accuses the pool once you
 * can see the pool. The empty state resolves against the *same slice* the
 * filters describe, so narrowing to a project answers for that project.
 */
export function WorkersPanel({
  workers,
  items,
  pools,
  projects,
  search,
  onSearchChange,
}: WorkersPanelProps) {
  const session = useSession()

  // Every filter except the promoted search, which lives in the URL when the
  // route is driving.
  const [ownFilters, setOwnFilters] = useState<DataTableFilterValues>({})
  const [localSearch, setLocalSearch] = useState("")
  // The image digest is off until somebody asks for it: it answers exactly one
  // question — why a worker is draining — and a column that is blank of meaning
  // on ten rows out of eleven is width the handle wants back.
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({ digest: false })
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})
  const [stopping, setStopping] = useState<Worker | null>(null)

  const drain = useDrainWorker()
  const forceStop = useForceStopWorker()

  const itemsById = useMemo(
    () => new Map(items.map((item) => [item.id, item])),
    [items]
  )

  const drainMutate = drain.mutate
  const onDrain = useCallback(
    (worker: Worker) => {
      drainMutate(worker.id)
    },
    [drainMutate]
  )

  // Force stop is the destructive one — it tears a container down mid-item —
  // so it opens the dialog rather than firing. Drain does not, because drain
  // loses nothing.
  const onForceStop = useCallback((worker: Worker) => {
    setStopping(worker)
  }, [])

  const columns = useMemo(
    () =>
      createWorkerColumns({
        projects,
        itemsById,
        session,
        drainingId: drain.isPending ? (drain.variables ?? null) : null,
        stoppingId: forceStop.isPending ? (forceStop.variables ?? null) : null,
        onDrain,
        onForceStop,
      }),
    [
      projects,
      itemsById,
      session,
      drain.isPending,
      drain.variables,
      forceStop.isPending,
      forceStop.variables,
      onDrain,
      onForceStop,
    ]
  )

  /* The filter the toolbar promotes to its search field, asked of the same
     declarations the toolbar reads — see the derivation rule on
     `DataTableToolbar`. */
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

  const rows = useMemo(
    () => applyDataFilters(workers, filters, columns),
    [workers, filters, columns]
  )

  const projectFilter = filters.projectId ?? ""
  const profileFilter = filters.profile ?? ""

  // The pool being asked about: the project filter applied and nothing else.
  // Non-zero here with an empty table means the *other* filters emptied it,
  // which is a different sentence from an empty pool.
  const poolSize = useMemo(
    () =>
      projectFilter
        ? workers.filter((worker) => worker.projectId === projectFilter).length
        : workers.length,
    [workers, projectFilter]
  )

  // Unclaimed work in the same slice the filters describe — so "no workers on
  // atlas" can say whether atlas is waiting on anything.
  const backlog = useMemo(
    () =>
      items.filter(
        (item) =>
          item.status === "queued" &&
          (!projectFilter || item.projectId === projectFilter) &&
          (!profileFilter || item.profile === profileFilter)
      ).length,
    [items, projectFilter, profileFilter]
  )

  const minIdle = useMemo(
    () => minIdleFor(pools, projectFilter),
    [pools, projectFilter]
  )

  const emptyKind = resolveWorkerEmpty({ poolSize, minIdle, backlog })
  const projectKey = projects.find((entry) => entry.id === projectFilter)?.key

  const clearFilters = useCallback(() => {
    onFiltersChange(emptyFilterValues(columns))
  }, [columns, onFiltersChange])

  const failure = drain.error ?? forceStop.error

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
            <span className={tableStyles.count} data-test="worker-count">
              {rows.length} of {workers.length} workers
            </span>
          }
        />
      </div>

      {failure ? (
        <p className={styles.failure} role="alert">
          {failure instanceof Error
            ? failure.message
            : "The pool did not take that."}{" "}
          Nothing changed — the worker is as it was.
        </p>
      ) : null}

      {rows.length === 0 ? (
        <div className={styles.emptyArea}>
          <WorkerEmpty
            kind={emptyKind}
            backlog={backlog}
            minIdle={minIdle}
            poolSize={poolSize}
            projectKey={projectKey}
            onClearFilters={emptyKind === "filtered" ? clearFilters : undefined}
          />
        </div>
      ) : (
        <div className={styles.tableArea}>
          <DataTable
            columns={columns}
            data={rows}
            getRowId={getWorkerId}
            density="compact"
            columnVisibility={columnVisibility}
            onColumnVisibilityChange={setColumnVisibility}
            sorting={sorting}
            onSortingChange={setSorting}
            columnSizing={columnSizing}
            onColumnSizingChange={setColumnSizing}
          />
        </div>
      )}

      <ConfirmDialog
        open={stopping !== null}
        danger
        title="Force stop this worker?"
        body={stopBody(stopping, itemsById)}
        confirmLabel="Force stop"
        cancelLabel="Leave it running"
        onConfirm={() => {
          if (stopping) {
            forceStop.mutate(stopping.id)
          }
          setStopping(null)
        }}
        onCancel={() => setStopping(null)}
      />
    </>
  )
}

/**
 * What the operator is actually about to do, in the two cases that differ.
 * A busy worker loses the item it is holding back to the queue; an idle one
 * loses nothing but itself. Saying which is the whole reason this act asks.
 */
function stopBody(
  worker: Worker | null,
  itemsById: Map<string, QueueItem>
): string {
  if (!worker) {
    return ""
  }
  const item = worker.itemId ? itemsById.get(worker.itemId) : undefined
  if (!item) {
    return `${worker.id} is idle. The container is torn down now, and scale raises another when there is work for it.`
  }
  return `${worker.id} is holding "${item.label}" on run ${item.runId}. The container is torn down now, the lease is released, and the item goes back to the queue for another worker to claim.`
}
