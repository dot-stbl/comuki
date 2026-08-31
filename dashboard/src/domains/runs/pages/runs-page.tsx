import { useCallback, useMemo, useRef, useState } from "react"
import { ChevronsDownUp, ChevronsUpDown, RotateCw } from "lucide-react"
import type { PanelImperativeHandle, PanelSize } from "react-resizable-panels"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useApproveRun, useCancelRun } from "@/domains/runs/api/mutations"
import { useRunsQuery } from "@/domains/runs/api/queries"
import { uniqueApps, uniqueProjects } from "@/domains/runs/model/filter-runs"
import {
  buildProfileFlow,
  triageOrder,
} from "@/domains/runs/model/profile-flow"
import type { RunSummary } from "@/domains/runs/model/types"
import { createRunColumns, getRunId } from "@/domains/runs/ui/runs-columns"
import {
  ProfileRiver,
  ProfileStrip,
  RiverLegend,
} from "@/domains/runs/ui/profile-river"
import tableStyles from "@/domains/runs/ui/runs-table.module.css"
import { can, projectOf, useSession } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  DataTable,
  DataTableToolbar,
  SplitPane,
  SplitPanel,
  SplitSeparator,
  Tooltip,
  applyDataFilters,
  dataFilterSpecs,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import styles from "./runs-page.module.css"

const SKELETON_HEIGHTS = [
  "72%",
  "58%",
  "64%",
  "88%",
  "76%",
  "40%",
  "92%",
  "54%",
  "30%",
]

/**
 * The collapsed board's height, in pixels because that is what the panel
 * measures in. Roughly one row: enough for the flow's shape, not enough for a
 * number or a label — which is the point of the strip. Mirrors `--h-strip`,
 * which is what any stylesheet that needs the same depth reads.
 */
const STRIP_HEIGHT = 34

/** The tab stays open all day, so the divider and the collapse survive reloads. */
const BOARD_LAYOUT_KEY = "comuki.runs.board"

export interface RunsPageProps {
  /**
   * The promoted search filter, held in the URL by the route.
   *
   * Controlled-or-not, the way the kit's own table takes `columnVisibility`:
   * hand both halves over and the URL owns the value; hand neither and the
   * screen keeps it in local state. The route hands both, which is what makes
   * a narrowed list a link somebody can paste into a ticket and what stops a
   * reload from throwing the filter away — and it is what lets the palette
   * hand free text off to this screen and have it land already narrowed.
   */
  search?: string
  onSearchChange?: (next: string) => void
}

export function RunsPage({ search, onSearchChange }: RunsPageProps = {}) {
  const { data = [], isLoading, isError, error, refetch } = useRunsQuery()

  const board = useRef<PanelImperativeHandle | null>(null)
  const [boardCollapsed, setBoardCollapsed] = useState(false)
  // Every filter *except* the promoted search, which lives in the URL when the
  // route is driving. Keeping them apart is what lets one value move house
  // without the other four learning about it.
  const [ownFilters, setOwnFilters] = useState<DataTableFilterValues>({})
  const [localSearch, setLocalSearch] = useState("")
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  // Widths the duty engineer dragged. Held here rather than inside the table
  // for the same reason sorting and visibility are: it is the screen's to keep,
  // and the day it should survive a reload it goes to the same store the split
  // layout already uses, with nothing to change in the kit.
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})
  const [cancelling, setCancelling] = useState<RunSummary | null>(null)

  const approve = useApproveRun()
  const cancel = useCancelRun()

  // The shift, not an answer about it. Both acts this screen offers are
  // decided per row against that row's project — the same person approves on
  // one and only watches the next — so the question cannot be asked here; only
  // the material for it can be handed down. See the note on
  // `RunColumnsOptions` for why it is the session and not a hook in the cell.
  const session = useSession()

  const flow = useMemo(() => buildProfileFlow(data), [data])
  const apps = useMemo(() => uniqueApps(data), [data])
  const projects = useMemo(
    () => uniqueProjects(data, session.projects),
    [data, session.projects]
  )
  // The board's own column order, flattened: the filter's options and the
  // profile column's sort rank are the same derived list, so the head and the
  // board can never disagree about where a profile sits in the pipeline.
  const profiles = flow.order

  // The button already refuses a denied click, but the handler answers the
  // same question again on the way in: the gate is the permission, not the
  // control that happens to be carrying it today. Both read the row's project,
  // never the shift.
  const approveMutate = approve.mutate
  const onApprove = useCallback(
    (run: RunSummary) => {
      if (!can(session, "plans.approve", run.projectId)) {
        return
      }
      approveMutate(run.id)
    },
    [approveMutate, session]
  )
  const onCancel = useCallback(
    (run: RunSummary) => {
      if (!can(session, "runs.stop", run.projectId)) {
        return
      }
      setCancelling(run)
    },
    [session]
  )

  const approvingId = approve.isPending ? (approve.variables ?? null) : null
  const cancellingId = cancel.isPending ? (cancel.variables ?? null) : null

  const columns = useMemo(
    () =>
      createRunColumns({
        apps,
        projects,
        profiles,
        approvingId,
        cancellingId,
        onApprove,
        onCancel,
        session,
      }),
    [
      apps,
      projects,
      profiles,
      approvingId,
      cancellingId,
      onApprove,
      onCancel,
      session,
    ]
  )

  /* Which filter the toolbar promotes to its search field — asked of the same
     declarations the toolbar reads rather than spelled out here, so the value
     the URL carries and the box it lands in can never come apart. See the
     derivation rule on `DataTableToolbar`: the first `text` filter a column
     set declares is the row's search. */
  const searchId = useMemo(
    () => dataFilterSpecs(columns).find((spec) => spec.filter.kind === "text")?.id,
    [columns]
  )

  const searchValue = onSearchChange ? (search ?? "") : localSearch
  const setSearchValue = onSearchChange ?? setLocalSearch

  // One bag for everything downstream: the toolbar renders it, the table is
  // filtered by it, and neither has to know that one of the values took a
  // different route to get here.
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

  // Every run, worst-first, minus whatever the toolbar is filtering out. The
  // board narrows this list by writing the `profile` filter — it is not a
  // precondition for the list existing.
  //
  // Triage stays the order the rows arrive in even once the head is sortable,
  // and the two compose rather than compete: the table sorts what it is given,
  // and TanStack breaks ties on the incoming index, so an explicit sort is the
  // primary key and triage is the tiebreak beneath it. Sort by app and each
  // app still reads worst-first; clear the sort — the third click — and the
  // screen is back to opening on the runs that need a human. No mode flag, and
  // nothing to unwind on the day this list is sorted server-side: `sorting`
  // goes to the query and `triageOrder` goes with it.
  const rows = useMemo(
    () => triageOrder(applyDataFilters(data, filters, columns)),
    [data, filters, columns]
  )

  const profileFilter = filters.profile ?? ""

  // Clicking the profile the table is already filtered to clears it, which is
  // what the node's pressed state promises.
  const onSelectProfile = useCallback((profile: string) => {
    setOwnFilters((current) => ({
      ...current,
      profile: current.profile === profile ? "" : profile,
    }))
  }, [])

  const onBoardResize = useCallback((size: PanelSize) => {
    setBoardCollapsed(size.inPixels <= STRIP_HEIGHT + 1)
  }, [])

  const expandBoard = useCallback(() => {
    board.current?.expand()
  }, [])

  const toggleBoard = useCallback(() => {
    const panel = board.current
    if (!panel) {
      return
    }
    if (panel.isCollapsed()) {
      panel.expand()
    } else {
      panel.collapse()
    }
  }, [])

  const failure = approve.error ?? cancel.error
  const ready = !isLoading && !isError && data.length > 0

  const emptyLabel = profileFilter
    ? "No runs on this profile."
    : hasActiveFilters(filters)
      ? "No runs match the current filters."
      : "No runs yet."

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: "live runs" }]}
          title="Live runs"
          summary={
            <>
              <span className={styles.strong}>{flow.total}</span> runs ·{" "}
              <span className={styles.strong}>{flow.runningTotal}</span> running
              · <span className={styles.warn}>{flow.blockedTotal}</span> waiting
              on a human
            </>
          }
          actions={
            ready ? (
              <>
                <RiverLegend />
                {/* Two words became a glyph, and the words came back on the
                    tooltip. The `aria-label` is the full sentence either way,
                    so the name a person was told to look for is the name the
                    control still answers to. */}
                <Tooltip
                  content={boardCollapsed ? "Expand flow" : "Collapse flow"}
                >
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    data-test="board-toggle"
                    aria-controls="board"
                    aria-expanded={!boardCollapsed}
                    aria-label={
                      boardCollapsed ? "Expand flow" : "Collapse flow"
                    }
                    onClick={toggleBoard}
                  >
                    {boardCollapsed ? (
                      <ChevronsUpDown aria-hidden="true" />
                    ) : (
                      <ChevronsDownUp aria-hidden="true" />
                    )}
                  </Button>
                </Tooltip>
              </>
            ) : null
          }
          /* The filter bar rides in the header rather than above the table.
             The header is the band that never scrolls, and these are the
             controls that decide which rows the screen is showing — a filter
             that scrolls away from the list it narrows is a filter the duty
             engineer has to go looking for. Nothing else moved: the screen
             still owns `filters` and `columnVisibility`, and the same values
             still reach the same table. See the contract on `PageHeader`. */
          filters={
            ready ? (
              <DataTableToolbar
                columns={columns}
                filters={filters}
                onFiltersChange={onFiltersChange}
                columnVisibility={columnVisibility}
                onColumnVisibilityChange={setColumnVisibility}
                trailing={
                  <span className={tableStyles.count}>{rows.length} shown</span>
                }
              />
            ) : null
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeletonRow} data-test="runs-loading">
            {SKELETON_HEIGHTS.map((height, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ height }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>Couldn&apos;t load runs</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="runs-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {!isLoading && !isError && data.length === 0 ? (
          <div className={styles.state}>
            <p className={styles.stateTitle}>No runs yet.</p>
          </div>
        ) : null}

        {ready ? (
          <SplitPane
            orientation="vertical"
            storageKey={BOARD_LAYOUT_KEY}
            className={styles.split}
          >
            <SplitPanel
              id="board"
              className={styles.boardPanel}
              panelRef={board}
              defaultSize="62%"
              minSize="18%"
              collapsible
              collapsedSize={STRIP_HEIGHT}
              onResize={onBoardResize}
            >
              {boardCollapsed ? (
                <ProfileStrip flow={flow} onExpand={expandBoard} />
              ) : flow.columns.length === 0 ? (
                // Tickets exist but the brain has planned none of them, so
                // there is no flow to draw yet. Saying that beats an empty
                // board, which reads as a screen that failed to load.
                <div className={styles.state}>
                  <p className={styles.stateTitle}>No plans yet</p>
                  <p className={styles.stateBody}>
                    These runs are accepted but the brain has not planned them.
                    The flow appears once the first plan has work items.
                  </p>
                </div>
              ) : (
                <div className={styles.flow}>
                  <ProfileRiver
                    flow={flow}
                    selected={profileFilter || null}
                    onSelect={onSelectProfile}
                  />
                </div>
              )}
            </SplitPanel>

            <SplitSeparator
              orientation="vertical"
              aria-label="Resize the flow board"
            />

            <SplitPanel id="table" className={styles.tablePanel} minSize="25%">
              {failure ? (
                <p className={styles.failure} role="alert">
                  {failure instanceof Error
                    ? failure.message
                    : "The decision failed."}{" "}
                  Nothing changed — the run is back as it was.
                </p>
              ) : null}

              <div className={styles.tableArea}>
                <DataTable
                  columns={columns}
                  data={rows}
                  getRowId={getRunId}
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
            </SplitPanel>
          </SplitPane>
        ) : null}
      </div>

      <ConfirmDialog
        open={cancelling !== null}
        danger
        title="Cancel this run?"
        body={
          cancelling
            ? // The project is named here for the same reason the row names it:
              // this list mixes them, and tearing down a container is the last
              // moment to notice it is the wrong project's.
              `${cancelling.title} · ${projectOf(session, cancelling.projectId)?.key ?? cancelling.app} — the container is torn down and the lease released. Work already merged stays.`
            : ""
        }
        confirmLabel="Cancel run"
        cancelLabel="Keep running"
        onConfirm={() => {
          if (cancelling && can(session, "runs.stop", cancelling.projectId)) {
            cancel.mutate(cancelling.id)
          }
          setCancelling(null)
        }}
        onCancel={() => setCancelling(null)}
      />
    </AppShell>
  )
}
