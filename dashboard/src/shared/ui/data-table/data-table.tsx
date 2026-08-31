import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  type Ref,
  type UIEvent as ReactUIEvent,
} from "react"
import {
  columnOrderingFeature,
  columnSizingFeature,
  columnVisibilityFeature,
  constructSortFn,
  createSortedRowModel,
  rowSelectionFeature,
  rowSortingFeature,
  sortFn_alphanumeric,
  sortFn_basic,
  sortFn_datetime,
  sortFn_text,
  tableFeatures,
  useTable,
  type Column,
  type ColumnDef,
  type ColumnOrderState,
  type ColumnSizingState,
  type ColumnVisibilityState,
  type CreatedSortFn,
  type RowData,
  type RowSelectionState,
  type SortDirection,
  type SortingState,
  type Updater,
} from "@tanstack/react-table"
import { useVirtualizer } from "@tanstack/react-virtual"
import { ChevronDown, ChevronUp } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import styles from "./data-table.module.css"

/**
 * The feature set every kit table registers. TanStack v9 has no global
 * features — a slice of state only exists when its feature is registered, so
 * this list *is* the primitive's capability surface: visibility and order for
 * the column manager, selection for the optional checkbox column, sorting for
 * the head, sizing for the resize grips. Filtering and pagination stay absent:
 * filtering is the screen's job (see `applyDataFilters`), and the body
 * virtualizes instead of paging.
 *
 * Sorting and sizing are registered here but are *not* owned here — like
 * visibility and order they are slices the screen holds, so the day the
 * orchestrator learns to sort server-side, or the day a duty engineer's column
 * widths follow them between machines, the swap is a call site handing the
 * slice to its query or its store rather than surgery on this component.
 *
 * `columnResizingFeature` is deliberately absent. It only carries the transient
 * drag bookkeeping and a mouse/touch-only handler; this component drives the
 * drag itself because the grip has to answer the keyboard too, and one handler
 * that serves both is one behaviour rather than two that can disagree.
 *
 * The `sortFns` registry names the built-ins `sortFn: "auto"` can resolve to;
 * naming only these four keeps the rest out of the bundle.
 */
const dataTableFeatures = tableFeatures({
  columnOrderingFeature,
  columnSizingFeature,
  columnVisibilityFeature,
  rowSelectionFeature,
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  sortFns: {
    alphanumeric: sortFn_alphanumeric,
    basic: sortFn_basic,
    datetime: sortFn_datetime,
    text: sortFn_text,
  },
})

export type DataTableFeatures = typeof dataTableFeatures

export type DataTableDensity = "compact" | "comfortable"

export interface DataFilterOption {
  value: string
  label: string
}

/**
 * A filter the screen declares once, on the column it belongs to.
 * `DataTableToolbar` renders the control; `applyDataFilters` applies the value.
 * The table itself never reads this.
 */
export type DataColumnFilter<TData extends RowData> =
  | {
      kind: "text"
      placeholder?: string
      /**
       * Custom predicate. Defaults to a case-insensitive substring match on
       * the column's own `accessorKey` field.
       */
      match?: (row: TData, needle: string) => boolean
    }
  | {
      kind: "select"
      options: readonly DataFilterOption[]
      /** Label for the "no filter" entry. Defaults to `all`. */
      placeholder?: string
      /** Custom predicate. Defaults to string equality on `accessorKey`. */
      match?: (row: TData, value: string) => boolean
    }

/**
 * Screen-level concerns that ride along with a column declaration. Layout
 * (`width`, `align`), voice (`numeric`) and filtering all belong to the screen
 * that owns the data, not to the table.
 */
export interface DataColumnMeta<TData extends RowData> {
  /**
   * The column's *initial* track width in pixels. Pixels, not a CSS length,
   * for the same reason `ROW_HEIGHT` is: once a column can be dragged, its
   * width is a number the user owns and a screen persists, and a declaration
   * in `rem` beside a drag result in `px` is two width sources that drift.
   * This one feeds `columnDef.size`, so `column.getSize()` — the single source
   * the head, the body and the table's own inline size all read — starts here
   * and stops here the moment `columnSizing` names the column.
   *
   * Omit for a flexible track: it shares the slack with the other flexible
   * columns exactly as before, until the user sizes it.
   */
  width?: number
  align?: "start" | "end"
  /**
   * Sticks the column to the start edge while the table scrolls sideways. A
   * declaration rather than a state slice, because nothing in the UI toggles
   * it — and because it is a flag rather than a position, the runs board can
   * pin the run id without reordering its columns.
   *
   * A pinned column never grows: its rendered width has to equal the width the
   * next pinned column offsets itself by.
   */
  pinned?: boolean
  /**
   * Set `false` to leave a column out of resizing — a fixed checkbox gutter,
   * say. Default is resizable, in tables whose screen owns `columnSizing`.
   */
  resizable?: boolean
  /**
   * Values in the data voice, tabular figures, aligned to the end edge — and
   * compared as numbers, because a column whose values *are* numbers may not
   * sort 10 before 9. That default is only a default: a column that merely
   * *renders* like a number (a formatted duration, say) declares its own
   * `sortFn` and keeps it.
   */
  numeric?: boolean
  filter?: DataColumnFilter<TData>
  /** Name in the column manager when `header` is not a plain string. */
  label?: string
}

/**
 * A comparator a column hands to `sortFn`. Build one with `keySort`,
 * `rankSort` or `numericSort` rather than reaching for TanStack directly —
 * screens should not have to know which table library is underneath.
 */
export type DataSortFn = CreatedSortFn<DataTableFeatures, RowData>

/** Values a comparator cannot rank sort after the ones it can. */
const UNRANKED = Number.MAX_SAFE_INTEGER

/**
 * Orders a column by a number the screen derives from the cell value, rather
 * than by how that value happens to spell. Every kit comparator is this: a
 * rank for an ordered enum, a parse for a formatted quantity.
 */
export function keySort(read: (value: unknown) => number): DataSortFn {
  return constructSortFn<DataTableFeatures, RowData>({
    resolveDataValue: read,
    sort: (a, b) => a - b,
  })
}

/**
 * Orders a column the way the domain means rather than the way the alphabet
 * does — `escalated` before `running` because that is triage order, not
 * because `e` precedes `r`. Values the map does not name sort last.
 */
export function rankSort(rank: Readonly<Record<string, number>>): DataSortFn {
  return keySort((value) => rank[String(value)] ?? UNRANKED)
}

/** Numbers compared as numbers. Blank and non-numeric cells sort last. */
export const numericSort: DataSortFn = keySort((value) => {
  if (value === null || value === undefined || value === "") {
    return UNRANKED
  }
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : UNRANKED
})

/** A TanStack column declaration carrying the kit's `meta`. */
export type DataColumn<TData extends RowData> = ColumnDef<
  DataTableFeatures,
  TData
> & {
  meta?: DataColumnMeta<TData>
}

/** Filter values keyed by column id. Empty string means "not filtering". */
export type DataTableFilterValues = Record<string, string>

export type DataTableColumnVisibility = ColumnVisibilityState
export type DataTableColumnOrder = ColumnOrderState
export type DataTableRowSelection = RowSelectionState
/** Pixel widths keyed by column id. A column absent from the map is at its
 *  declared `meta.width`, which is what makes reset a delete. */
export type DataTableColumnSizing = ColumnSizingState
/** `[]` is unsorted. Single-column by contract — see `enableMultiSort`. */
export type DataTableSorting = SortingState

export interface DataTableSelection {
  /** Selected row ids, keyed by the id `getRowId` returns. */
  value: DataTableRowSelection
  onChange: (next: DataTableRowSelection) => void
  /** Accessible noun for the checkboxes, e.g. `"run"`. Defaults to `"row"`. */
  noun?: string
}

export interface DataTableProps<TData extends RowData> {
  /** Stable reference — declare at module scope or memoize. */
  columns: DataColumn<TData>[]
  /** Stable reference — memoize derived lists. */
  data: TData[]
  /** Row identity. Virtualized rows key off this, so it must be stable. */
  getRowId: (row: TData) => string
  density?: DataTableDensity
  /**
   * The frame's depth as a CSS length. Defaults to `--h-table-body`. It caps
   * the whole instrument — the two hairlines of the frame included — and the
   * scroll port takes what is left, so a screen that hands the table a height
   * gets a bottom edge exactly there rather than a rule floating a pixel past
   * it.
   */
  bodyHeight?: string
  /** Screen-owned; pair with `onColumnVisibilityChange`. */
  columnVisibility?: DataTableColumnVisibility
  onColumnVisibilityChange?: (next: DataTableColumnVisibility) => void
  /** Screen-owned; pair with `onColumnOrderChange`. */
  columnOrder?: DataTableColumnOrder
  onColumnOrderChange?: (next: DataTableColumnOrder) => void
  /**
   * Screen-owned; pair with `onSortingChange`. Passing `onSortingChange` is
   * what makes the head interactive — a table whose screen holds no sorting
   * state keeps a plain, inert head rather than quietly sorting behind the
   * screen's back.
   */
  sorting?: DataTableSorting
  onSortingChange?: (next: DataTableSorting) => void
  /**
   * Screen-owned; pair with `onColumnSizingChange`. Passing the handler is what
   * puts a grip on every resizable head — a table whose screen holds no sizing
   * state keeps the widths its columns declared, exactly as before.
   */
  columnSizing?: DataTableColumnSizing
  onColumnSizingChange?: (next: DataTableColumnSizing) => void
  /** Omit for a table without checkboxes. */
  selection?: DataTableSelection
  onRowClick?: (row: TData) => void
  emptyLabel?: string
  className?: string
  ref?: Ref<HTMLDivElement>
}

const SELECT_COLUMN_ID = "__select"

/**
 * Row heights in pixels, because the virtualizer positions rows in pixels and
 * a rem-to-pixel guess would drift the moment the root font size moved. The
 * number is the single source of truth: the component publishes it to CSS as
 * `--dt-row-h`, so the rule the browser paints and the offset the virtualizer
 * computes can never disagree. Rows stay uniform — cells are single-line by
 * contract, which is also what makes a 200-row duty list scroll smoothly.
 *
 * Compact matches `--h-row-head` (2rem): the head is a row of the same depth as
 * the rows under it, so the band terminates on the same rhythm rather than
 * standing a little taller than everything it names. Comfortable is one step of
 * the widened scale above that. Both moved up when the spacing scale did — a
 * row was the tightest thing in the product and is now a line of values with
 * air around it.
 */
const ROW_HEIGHT: Record<DataTableDensity, number> = {
  compact: 32,
  comfortable: 40,
}

/** Rows kept alive beyond the viewport. Also absorbs the sticky head's offset. */
const OVERSCAN = 12

/**
 * Column geometry, in pixels for the same reason `ROW_HEIGHT` is: a resized
 * column's width is a number the pointer produced and a screen may persist, so
 * the declaration, the drag and the painted track all speak one unit.
 *
 * `MIN_COLUMN_W` is the floor a drag cannot cross — narrower than this a head
 * label is gone and the column reads as a rendering fault. A column that
 * declares itself narrower than the floor (the checkbox gutter) keeps its own
 * width as its floor instead, so the clamp can never widen a declaration.
 *
 * `FLEX_COLUMN_W` is what a column without `meta.width` contributes to the
 * table's inline size. It is a *basis*, not a width: such a column still grows
 * into whatever slack the port leaves, exactly as it did before sizing was
 * explicit. It becomes the real width only when the table is already wider than
 * its port, where there is no slack to share.
 */
const MIN_COLUMN_W = 56
const MAX_COLUMN_W = 720
const FLEX_COLUMN_W = 96

/** Arrow key step, and the coarse step Shift asks for. */
const RESIZE_STEP = 8
const RESIZE_STEP_COARSE = 32

/** `aria-sort` belongs on the `th`, and speaks its own three words. */
const ARIA_SORT: Record<SortDirection | "none", "ascending" | "descending" | "none"> =
  {
    asc: "ascending",
    desc: "descending",
    none: "none",
  }

function resolveUpdater<T>(updater: Updater<T>, current: T): T {
  return typeof updater === "function"
    ? (updater as (old: T) => T)(current)
    : updater
}

/**
 * `columnDef.meta` is typed by TanStack as the declaration-merged `ColumnMeta`
 * (empty here, since the kit does not augment a vendor global). The kit's own
 * shape is attached through `DataColumn`, so reading it back needs this one
 * cast — kept in a single named place rather than sprinkled at each use.
 */
function metaOf<TData extends RowData>(columnDef: {
  meta?: unknown
}): DataColumnMeta<TData> | undefined {
  return columnDef.meta as DataColumnMeta<TData> | undefined
}

/**
 * The one place a track's width is decided, read by the head, by every body
 * cell and by the table's own inline size. `column.getSize()` is the number —
 * `columnSizing[id]` when the user has sized the column, `meta.width` when it
 * only declared one, `FLEX_COLUMN_W` when it declared neither — already clamped
 * by TanStack against the min and max this component attaches. Header and body
 * cannot disagree because there is nothing for them to disagree about: they
 * call this function with the same `Column` instance.
 *
 * `flex-grow` is the one thing the number does not settle. A column that
 * declared no width and has not been sized keeps growing into the port's slack
 * the way it always has; the moment it is pinned or sized it stops, because a
 * pinned column's rendered width is what the next pinned column offsets itself
 * by, and a dragged column that then grew would not be the width it was
 * dragged to. `flex-shrink` is always 0: the table is exactly as wide as its
 * columns, so there is never anything to shrink into.
 */
function trackStyle<TData extends RowData>(
  column: Column<DataTableFeatures, TData>,
  sizing: DataTableColumnSizing,
  pinnedStart: number | undefined
): CSSProperties {
  const meta = metaOf<TData>(column.columnDef)
  const grows =
    meta?.width === undefined &&
    !meta?.pinned &&
    sizing[column.id] === undefined
  return {
    flex: `${grows ? 1 : 0} 0 ${column.getSize()}px`,
    ...(pinnedStart === undefined
      ? {}
      : { insetInlineStart: `${pinnedStart}px` }),
  }
}

/**
 * Declared widths become TanStack's sizing bounds. `size` is the initial width
 * a reset returns to, `minSize` the floor a drag cannot cross, `maxSize` a
 * ceiling so a wild drag cannot hand the row a five-figure track.
 */
function withSize<TData extends RowData>(
  column: DataColumn<TData>
): DataColumn<TData> {
  const size = metaOf<TData>(column)?.width ?? FLEX_COLUMN_W
  return {
    ...column,
    size,
    minSize: Math.min(MIN_COLUMN_W, size),
    maxSize: MAX_COLUMN_W,
  }
}

function alignClass<TData extends RowData>(
  meta: DataColumnMeta<TData> | undefined
) {
  const align = meta?.align ?? (meta?.numeric ? "end" : undefined)
  return align === "end" ? styles.alignEnd : undefined
}

/**
 * `meta.numeric` says the values are numbers, which decides how they compare
 * as much as how they are set. Deriving the comparator here means a screen
 * declares that once; a column that means something else by `numeric` says so
 * with its own `sortFn`, and that always wins.
 */
function withNumericSort<TData extends RowData>(
  column: DataColumn<TData>
): DataColumn<TData> {
  const meta = metaOf<TData>(column)
  if (!meta?.numeric || "sortFn" in column) {
    return column
  }
  return { ...column, sortFn: numericSort }
}

/**
 * Human name for a live column, resolved the way `dataColumnLabel` resolves it
 * for a declaration: `meta.label`, then a plain-string `header`, then the id.
 * The grip's accessible name is built from this, so "Resize cost column" says
 * the same word the head does.
 */
function columnLabel<TData extends RowData>(
  column: Column<DataTableFeatures, TData>
): string {
  const meta = metaOf<TData>(column.columnDef)
  if (meta?.label) {
    return meta.label
  }
  const header = (column.columnDef as { header?: unknown }).header
  return typeof header === "string" ? header : column.id
}

/**
 * The width the user is about to change, measured rather than modelled. A
 * column that grows renders wider than the basis the model holds, so starting
 * a drag from the model would make the track jump under the pointer on the
 * first pixel. `getBoundingClientRect` is the truth the user is looking at;
 * `column.getSize()` is the fallback for a tree that has no layout at all —
 * jsdom, or a column inside a collapsed panel — where the model is all there
 * is and the measurement would read a misleading zero.
 */
function renderedWidth<TData extends RowData>(
  cell: HTMLElement | null,
  column: Column<DataTableFeatures, TData>
): number {
  const measured = cell?.getBoundingClientRect().width ?? 0
  return measured > 0 ? measured : column.getSize()
}

function clampWidth(next: number): number {
  return Math.round(Math.min(MAX_COLUMN_W, Math.max(MIN_COLUMN_W, next)))
}

interface ColumnGripProps<TData extends RowData> {
  column: Column<DataTableFeatures, TData>
  onResize: (next: number) => void
  onReset: () => void
}

/**
 * The handle on a head's end edge. A real button rather than a bare `div`, so
 * it is in the tab order, takes the kit's focus ring and announces itself
 * without a role attribute: pointer drags it, arrow keys step it, Shift makes
 * the step coarse, and activating it — double-click, Enter or Space — puts the
 * column back to the width it declared.
 *
 * The drag runs on pointer events with capture, so one code path covers mouse,
 * pen and touch, and a pointer that leaves the header — which it will, the
 * moment the column narrows past it — keeps delivering moves to this element
 * rather than to whatever it is now over. No animation: a drag is direct
 * manipulation, and a track that eases toward the pointer is a track that is
 * not under it.
 */
function ColumnGrip<TData extends RowData>({
  column,
  onResize,
  onReset,
}: ColumnGripProps<TData>) {
  const drag = useRef<{
    pointerId: number
    originX: number
    startWidth: number
    direction: 1 | -1
  } | null>(null)

  const headOf = (grip: HTMLElement) => grip.closest("th")

  /**
   * Which way "wider" points on screen. Everything else here is logical —
   * `inset-inline-end`, `insetInlineStart` — but a pointer delta and an arrow
   * key are physical, and in a right-to-left table the end edge a column grows
   * from is the left one.
   */
  const outward = (head: HTMLElement | null): 1 | -1 =>
    head && getComputedStyle(head).direction === "rtl" ? -1 : 1

  const onPointerDown = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (event.button !== 0) {
      return
    }
    const head = headOf(event.currentTarget)
    drag.current = {
      pointerId: event.pointerId,
      originX: event.clientX,
      startWidth: renderedWidth(head, column),
      direction: outward(head),
    }
    event.currentTarget.setPointerCapture?.(event.pointerId)
    // Without this the drag selects the head's text on the way past. It also
    // costs the button the focus a click would have given it, so take that
    // back explicitly: a column that was just dragged is the one a user wants
    // to nudge with the arrows.
    event.preventDefault()
    event.currentTarget.focus()
  }

  const onPointerMove = (event: ReactPointerEvent<HTMLButtonElement>) => {
    const state = drag.current
    if (!state || state.pointerId !== event.pointerId) {
      return
    }
    const delta = (event.clientX - state.originX) * state.direction
    if (!Number.isFinite(delta)) {
      return
    }
    onResize(clampWidth(state.startWidth + delta))
  }

  const endDrag = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (drag.current?.pointerId === event.pointerId) {
      drag.current = null
    }
  }

  const onKeyDown = (event: ReactKeyboardEvent<HTMLButtonElement>) => {
    const head = headOf(event.currentTarget)
    const step = (event.shiftKey ? RESIZE_STEP_COARSE : RESIZE_STEP) * outward(head)
    const from = renderedWidth(head, column)

    if (event.key === "ArrowRight") {
      event.preventDefault()
      onResize(clampWidth(from + step))
      return
    }
    if (event.key === "ArrowLeft") {
      event.preventDefault()
      onResize(clampWidth(from - step))
      return
    }
    // Swallow the activation rather than let it reach `click`, so the keyboard
    // and the double-click reach reset through exactly one path.
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault()
      onReset()
    }
  }

  return (
    <button
      type="button"
      className={styles.grip}
      data-test={`data-table-resize-${column.id}`}
      aria-label={`Resize ${columnLabel(column)} column`}
      title="Drag to resize, double-click to reset"
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onLostPointerCapture={endDrag}
      onKeyDown={onKeyDown}
      onDoubleClick={onReset}
    />
  )
}

function selectionColumn<TData extends RowData>(
  noun: string
): DataColumn<TData> {
  return {
    id: SELECT_COLUMN_ID,
    enableHiding: false,
    // The box (`--s5`) plus its own tight gutter either side (`--s2`) — a fixed
    // track with nothing to gain from being dragged, so it opts out of resizing
    // rather than carrying a grip that would be wider than the column. Pixels,
    // like every other track width, and it has to agree with `.gutter` in the
    // stylesheet: this width is what the next pinned column offsets itself by,
    // so a checkbox that overflowed it would be clipped rather than move it.
    meta: { width: 28, resizable: false, label: "Select" },
    header: ({ table }) => {
      const all = table.getIsAllRowsSelected()
      const some = table.getIsSomeRowsSelected()
      return (
        <input
          type="checkbox"
          className={styles.check}
          data-test="data-table-select-all"
          aria-label={`Select all ${noun}s`}
          checked={all}
          ref={(node) => {
            if (node) {
              node.indeterminate = !all && some
            }
          }}
          onChange={table.getToggleAllRowsSelectedHandler()}
        />
      )
    },
    cell: ({ row }) => (
      <input
        type="checkbox"
        className={styles.check}
        data-test="data-table-select-row"
        aria-label={`Select ${noun} ${row.id}`}
        checked={row.getIsSelected()}
        onChange={row.getToggleSelectedHandler()}
        onClick={(event) => {
          event.stopPropagation()
        }}
      />
    ),
  }
}

/**
 * Virtualized table primitive: sticky head, no pagination, no toolbar, no
 * filter row. Everything a screen might want to configure — filters, column
 * visibility and order, sorting, selection — is state the screen owns and
 * passes in.
 *
 * Filters live in the sibling `DataTableToolbar`, which reads the very same
 * column declarations, so a screen declares a filter once and gets both the
 * control and the predicate.
 */
export function DataTable<TData extends RowData>({
  columns,
  data,
  getRowId,
  density = "compact",
  bodyHeight,
  columnVisibility,
  onColumnVisibilityChange,
  columnOrder,
  onColumnOrderChange,
  sorting,
  onSortingChange,
  columnSizing,
  onColumnSizingChange,
  selection,
  onRowClick,
  emptyLabel = "No rows",
  className,
  ref,
}: DataTableProps<TData>) {
  const scrollRef = useRef<HTMLDivElement>(null)
  // Whether the port is scrolled off its start edge, and nothing more: the
  // pinned seam has to read as "there is a column hiding under here", which is
  // only true once something is. Flipping a boolean at the 0/non-0 boundary
  // costs one render per crossing — React bails out on the rest.
  const [scrolledX, setScrolledX] = useState(false)

  /**
   * How wide the scroll port actually is, watched rather than read once.
   *
   * The rail collapses, the split divider moves and the window resizes, and all
   * three change the port without remounting the table — so a measurement taken
   * at mount would be a measurement of a board the operator has since changed.
   * Only one decision reads this (whether the pins still fit, below), and the
   * state only moves when the number does, so a drag of the divider costs a
   * render per pixel it actually changes and nothing when it does not.
   *
   * 0 is "not measured": jsdom lays nothing out and `clientWidth` is 0 there,
   * and the first paint has not observed anything yet. Both must read as
   * "assume there is room", never as "the port is zero wide".
   */
  const [portWidth, setPortWidth] = useState(0)

  useEffect(() => {
    const port = scrollRef.current
    if (!port || typeof ResizeObserver === "undefined") {
      return
    }
    const observer = new ResizeObserver(() => {
      setPortWidth(port.clientWidth)
    })
    observer.observe(port)
    setPortWidth(port.clientWidth)
    return () => {
      observer.disconnect()
    }
  }, [])

  // Depend on the primitive bits, not the `selection` object: a screen that
  // inlines `selection={{ value, onChange }}` would otherwise rebuild the
  // column list — and with it the whole table model — on every render.
  const selectionNoun = selection?.noun ?? "row"
  const hasSelection = selection !== undefined

  const sortable = onSortingChange !== undefined
  const resizable = onColumnSizingChange !== undefined

  const tableColumns = useMemo<DataColumn<TData>[]>(() => {
    const declared = columns.map(withNumericSort).map(withSize)
    if (!hasSelection) {
      return declared
    }
    // The checkbox travels with whatever the screen pinned. A gutter that
    // scrolls away while a pinned column sticks in front of it leaves a row you
    // can read and cannot select, so the one declaration carries both.
    const anyPinned = columns.some((column) => metaOf<TData>(column)?.pinned)
    const select = selectionColumn<TData>(selectionNoun)
    return [
      withSize(
        anyPinned ? { ...select, meta: { ...select.meta, pinned: true } } : select
      ),
      ...declared,
    ]
  }, [columns, hasSelection, selectionNoun])

  const table = useTable<DataTableFeatures, TData>({
    features: dataTableFeatures,
    data,
    columns: tableColumns,
    getRowId,
    enableRowSelection: hasSelection,
    enableSorting: sortable,
    // One column at a time: a second sort key the head cannot show is a state
    // the user cannot read back, and `aria-sort` on two columns says nothing
    // about which of them wins.
    enableMultiSort: false,
    // The cycle the head promises: ascending, descending, back to none.
    sortDescFirst: false,
    // Only slices the screen actually controls are declared here; declaring a
    // slice without writing it back is how v9 tables silently stop updating.
    state: {
      ...(columnVisibility ? { columnVisibility } : {}),
      ...(columnOrder ? { columnOrder } : {}),
      ...(sorting ? { sorting } : {}),
      ...(columnSizing ? { columnSizing } : {}),
      ...(selection ? { rowSelection: selection.value } : {}),
    },
    ...(onColumnVisibilityChange && {
      onColumnVisibilityChange: (updater: Updater<ColumnVisibilityState>) => {
        onColumnVisibilityChange(
          resolveUpdater(updater, columnVisibility ?? {})
        )
      },
    }),
    ...(onColumnOrderChange && {
      onColumnOrderChange: (updater: Updater<ColumnOrderState>) => {
        onColumnOrderChange(resolveUpdater(updater, columnOrder ?? []))
      },
    }),
    ...(onSortingChange && {
      onSortingChange: (updater: Updater<SortingState>) => {
        onSortingChange(resolveUpdater(updater, sorting ?? []))
      },
    }),
    ...(onColumnSizingChange && {
      onColumnSizingChange: (updater: Updater<ColumnSizingState>) => {
        onColumnSizingChange(resolveUpdater(updater, columnSizing ?? {}))
      },
    }),
    ...(selection && {
      onRowSelectionChange: (updater: Updater<RowSelectionState>) => {
        selection.onChange(resolveUpdater(updater, selection.value))
      },
    }),
  })

  // The end of the row-model pipeline, so this is already sorted. The
  // virtualizer measures off exactly this list and nothing else — sorting
  // reorders rows, it does not add any.
  const rows = table.getRowModel().rows
  const rowHeight = ROW_HEIGHT[density]

  // The virtualizer's getters read live scroll state, so they are meant to be
  // re-read every render rather than memoized. React Compiler is not part of
  // this build (no `babel-plugin-react-compiler`), so the plugin's warning has
  // nothing to act on here — opt out rather than leave a standing warning.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => rowHeight,
    getItemKey: (index) => rows[index]?.id ?? index,
    overscan: OVERSCAN,
  })

  const leafColumns = table.getVisibleLeafColumns()
  const columnCount = leafColumns.length

  // Sizes as the model holds them, so a cell can tell "declared" from "the user
  // dragged it here" — the only thing `column.getSize()` cannot say, and the
  // only thing left that decides whether a track grows.
  const sizing = table.state.columnSizing

  /**
   * Where each pinned column parks, and which one carries the seam. The offset
   * is the sum of the pinned columns *before* it, not of every column before
   * it: pinning is a flag, so the pinned run need not start at the first
   * column, and an unpinned column ahead of a pinned one simply slides under it
   * — which is the whole point of pinning the run id rather than the badge in
   * front of it.
   */
  const pinnedStarts = new Map<string, number>()
  let pinnedWidth = 0
  for (const column of leafColumns) {
    if (metaOf<TData>(column.columnDef)?.pinned) {
      pinnedStarts.set(column.id, pinnedWidth)
      pinnedWidth += column.getSize()
    }
  }

  /**
   * Pinning is suspended when the port is too narrow to hold it.
   *
   * A pin is a promise that the column stays while the rest scrolls past it.
   * That promise is only keepable while there is a "rest" left to see: the
   * declared pins on the wider screens run 224px (runs) to 316px (sources, and
   * 344px once a screen turns selection on), and a port narrower than that plus
   * one readable column is a port where the pinned block covers everything.
   * Scrolling sideways then moves columns that are permanently underneath the
   * pins — the reading is not degraded, it is gone, and no gesture recovers it.
   *
   * So below the floor the pins simply come off and the table becomes an
   * ordinary sideways-scrolling grid where every column can be reached. That is
   * the Shape-Not-Reading Rule read the right way round: when space runs out
   * the table scrolls to keep its reading, and a pin that eats the port is the
   * thing destroying the reading, not the thing preserving it.
   *
   * `portWidth` is 0 until the observer has measured — on the first paint, and
   * for good in a tree with no layout — and 0 means "unknown", which keeps the
   * pins. A table must never lose a declared pin because nobody measured yet.
   */
  const holdsPins = portWidth === 0 || pinnedWidth + MIN_COLUMN_W <= portWidth
  if (!holdsPins) {
    pinnedStarts.clear()
    pinnedWidth = 0
  }

  const resizeColumn = (column: Column<DataTableFeatures, TData>) => ({
    onResize: (next: number) => {
      table.setColumnSizing((old) => ({ ...old, [column.id]: next }))
    },
    onReset: () => {
      column.resetSize()
    },
  })

  const cellProps = (column: Column<DataTableFeatures, TData>) => {
    const pinnedStart = pinnedStarts.get(column.id)
    return {
      style: trackStyle(column, sizing, pinnedStart),
      pinned: pinnedStart !== undefined,
      // The checkbox track holds a control rather than text, so it takes its
      // own gutter at every density instead of the one sized for values.
      gutter: column.id === SELECT_COLUMN_ID,
    }
  }

  return (
    <div
      ref={ref}
      data-test="data-table"
      data-density={density}
      className={cn(styles.root, styles[density], className)}
      // The seam is drawn against the frame, so the frame is what knows whether
      // the port has moved. It was on the port while the seam was a per-cell
      // shadow inside it; a continuous seam is a sibling of the port, not a
      // descendant, and an attribute has to be readable from the thing it
      // styles.
      data-scrolled-x={scrolledX ? "" : undefined}
      style={
        {
          // The pinned block's rendered width, published for the seam the same
          // way `--dt-row-h` is published for the virtualizer: one number, so
          // the edge the browser paints and the offset the component computed
          // cannot disagree. Exact rather than approximate — a pinned column
          // never takes `flex-grow`, so the sum of `getSize()` is the width.
          "--dt-pinned-w": `${pinnedWidth}px`,
          // The cap is the frame's, not the port's: the two hairlines are part
          // of the instrument, so a screen's depth has to contain them.
          ...(bodyHeight ? { maxBlockSize: bodyHeight } : {}),
        } as CSSProperties
      }
    >
      <div
        ref={scrollRef}
        className={styles.scroll}
        onScroll={(event: ReactUIEvent<HTMLDivElement>) => {
          setScrolledX(Math.abs(event.currentTarget.scrollLeft) > 0)
        }}
        style={
          {
            "--dt-row-h": `${rowHeight}px`,
            // The single width source, published once: the table is exactly as
            // wide as the sum of its tracks, floored at the port in CSS. Head
            // and body are separate elements but both are 100% of *this*, so
            // they cannot end in different places.
            "--dt-total-w": `${table.getTotalSize()}px`,
          } as CSSProperties
        }
      >
        <table className={styles.table}>
          <thead className={styles.head}>
            {table.getHeaderGroups().map((group) => (
              <tr key={group.id} className={styles.headRow}>
                {group.headers.map((header) => {
                  const column = header.column
                  const meta = metaOf<TData>(column.columnDef)
                  const canSort = column.getCanSort()
                  const sorted = canSort ? column.getIsSorted() : false
                  const canResize = resizable && meta?.resizable !== false
                  const track = cellProps(column)
                  const label = header.isPlaceholder ? null : (
                    <table.FlexRender header={header} />
                  )
                  return (
                    <th
                      key={header.id}
                      scope="col"
                      data-pinned={track.pinned ? "" : undefined}
                      className={cn(
                        styles.cell,
                        styles.th,
                        alignClass(meta),
                        track.gutter && styles.gutter,
                        track.pinned && styles.pinned
                      )}
                      style={track.style}
                      // The state belongs to the cell, not to the control
                      // inside it: a screen reader announces the column as
                      // sorted, then reads the button that changes it.
                      aria-sort={
                        canSort
                          ? ARIA_SORT[sorted === false ? "none" : sorted]
                          : undefined
                      }
                    >
                      {canSort && !header.isPlaceholder ? (
                        <button
                          type="button"
                          className={styles.sort}
                          data-test={`data-table-sort-${column.id}`}
                          data-sorted={sorted === false ? undefined : sorted}
                          onClick={column.getToggleSortingHandler()}
                        >
                          <span className={styles.sortLabel}>{label}</span>
                          {/* Idle heads point the way the first click sorts. */}
                          {sorted === "desc" ? (
                            <ChevronDown
                              className={styles.sortIcon}
                              aria-hidden="true"
                            />
                          ) : (
                            <ChevronUp
                              className={styles.sortIcon}
                              aria-hidden="true"
                            />
                          )}
                        </button>
                      ) : (
                        label
                      )}
                      {canResize ? (
                        <ColumnGrip column={column} {...resizeColumn(column)} />
                      ) : null}
                    </th>
                  )
                })}
              </tr>
            ))}
          </thead>

          {rows.length === 0 ? (
            <tbody className={styles.emptyBody}>
              <tr className={styles.emptyRow}>
                <td
                  className={styles.empty}
                  colSpan={Math.max(columnCount, 1)}
                  data-test="data-table-empty"
                >
                  {emptyLabel}
                </td>
              </tr>
            </tbody>
          ) : (
            <tbody
              className={styles.body}
              style={{ blockSize: `${virtualizer.getTotalSize()}px` }}
            >
              {virtualizer.getVirtualItems().map((item) => {
                const row = rows[item.index]
                if (!row) {
                  return null
                }
                return (
                  <tr
                    key={row.id}
                    data-test="data-table-row"
                    data-selected={row.getIsSelected() ? "" : undefined}
                    className={cn(
                      styles.row,
                      onRowClick && styles.clickable,
                      row.getIsSelected() && styles.selected
                    )}
                    /* Offset, not `translateY`. A transform would make every
                       row the containing block and a stacking context for the
                       cells inside it, which is precisely the ground a sticky
                       pinned cell stands on; `item.start` is the same number
                       either way, so virtualization does not notice. */
                    style={{ insetBlockStart: `${item.start}px` }}
                    onClick={
                      onRowClick ? () => onRowClick(row.original) : undefined
                    }
                  >
                    {row.getVisibleCells().map((cell) => {
                      const meta = metaOf<TData>(cell.column.columnDef)
                      const track = cellProps(cell.column)
                      return (
                        <td
                          key={cell.id}
                          data-pinned={track.pinned ? "" : undefined}
                          className={cn(
                            styles.cell,
                            styles.td,
                            meta?.numeric && styles.numeric,
                            alignClass(meta),
                            track.gutter && styles.gutter,
                            track.pinned && styles.pinned
                          )}
                          style={track.style}
                        >
                          <table.FlexRender cell={cell} />
                        </td>
                      )
                    })}
                  </tr>
                )
              })}
            </tbody>
          )}
        </table>
      </div>

      {/* One seam for the whole frame, and only when something is held. Drawn
          last so it is the last thing in the frame's own paint order as well as
          the top rung of the ladder, and rendered conditionally rather than
          hidden with a zero offset: a seam sitting on the frame's left edge is
          a hairline nobody asked for. */}
      {pinnedWidth > 0 ? (
        <span className={styles.seam} data-test="data-table-seam" aria-hidden="true" />
      ) : null}
    </div>
  )
}

/** Column id, resolved the way TanStack resolves it. */
export function dataColumnId<TData extends RowData>(
  column: DataColumn<TData>
): string {
  const shape = column as { id?: string; accessorKey?: string }
  return String(shape.id ?? shape.accessorKey ?? "")
}

/** Human name for a column: `meta.label`, a string header, then the id. */
export function dataColumnLabel<TData extends RowData>(
  column: DataColumn<TData>
): string {
  const meta = metaOf<TData>(column)
  if (meta?.label) {
    return meta.label
  }
  const header = (column as { header?: unknown }).header
  return typeof header === "string" ? header : dataColumnId(column)
}

export interface DataFilterSpec<TData extends RowData> {
  id: string
  label: string
  /** Field the default predicate reads, when the column declares one. */
  field?: string
  filter: DataColumnFilter<TData>
}

/**
 * The filters a column set declares, in declaration order. `DataTableToolbar`
 * renders from this; `applyDataFilters` evaluates from it. One declaration,
 * two consumers.
 */
export function dataFilterSpecs<TData extends RowData>(
  columns: DataColumn<TData>[]
): DataFilterSpec<TData>[] {
  const specs: DataFilterSpec<TData>[] = []
  for (const column of columns) {
    const filter = metaOf<TData>(column)?.filter
    if (!filter) {
      continue
    }
    specs.push({
      id: dataColumnId(column),
      label: dataColumnLabel(column),
      field: (column as { accessorKey?: string }).accessorKey,
      filter,
    })
  }
  return specs
}

/** A blank filter value per declared filter — handy for reset. */
export function emptyFilterValues<TData extends RowData>(
  columns: DataColumn<TData>[]
): DataTableFilterValues {
  const values: DataTableFilterValues = {}
  for (const spec of dataFilterSpecs(columns)) {
    values[spec.id] = ""
  }
  return values
}

/** True when at least one filter carries a value. */
export function hasActiveFilters(filters: DataTableFilterValues): boolean {
  return Object.values(filters).some((value) => value !== "")
}

/**
 * Client-side filtering for a screen holding its own rows. Text matches a
 * case-insensitive substring of the column's field, select matches it exactly,
 * and either can override that with `filter.match`. Screens backed by a server
 * skip this and hand the values to their query instead.
 */
export function applyDataFilters<TData extends RowData>(
  rows: TData[],
  filters: DataTableFilterValues,
  columns: DataColumn<TData>[]
): TData[] {
  const active = dataFilterSpecs(columns).filter(
    (spec) => (filters[spec.id] ?? "") !== ""
  )
  if (active.length === 0) {
    return rows
  }

  return rows.filter((row) =>
    active.every((spec) => {
      const value = filters[spec.id] ?? ""
      if (spec.filter.match) {
        return spec.filter.match(row, value)
      }
      const cell = spec.field
        ? (row as Record<string, unknown>)[spec.field]
        : undefined
      if (spec.filter.kind === "select") {
        return String(cell ?? "") === value
      }
      return String(cell ?? "")
        .toLowerCase()
        .includes(value.toLowerCase())
    })
  )
}
