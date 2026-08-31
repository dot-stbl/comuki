import { useId, type CSSProperties, type ReactNode } from "react"
import type { RowData } from "@tanstack/react-table"
import {
  ChevronDown,
  Filter,
  FilterX,
  Search,
  SlidersHorizontal,
  X,
} from "lucide-react"
import {
  Button as AriaButton,
  Dialog,
  DialogTrigger,
  Popover,
} from "react-aria-components"

import { cn } from "@/shared/lib/utils"

import { Button } from "../button"
import { Select } from "../select"
import { Tooltip } from "../tooltip"
import {
  dataColumnId,
  dataColumnLabel,
  dataFilterSpecs,
  hasActiveFilters,
  type DataColumn,
  type DataColumnFilter,
  type DataFilterSpec,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
} from "./data-table"
import styles from "./data-table-toolbar.module.css"

/** Widest the filter sheet lays its fields out. Two reads as a sheet; three
 *  reads as a form, and the sheet is meant to be glanced at, not filled in. */
const SHEET_COLUMNS = 2

export interface DataTableToolbarProps<TData extends RowData> {
  /** The same declarations the table gets — filters come from their `meta`. */
  columns: DataColumn<TData>[]
  filters: DataTableFilterValues
  onFiltersChange: (next: DataTableFilterValues) => void
  /** Pass both to fold the column manager into the bar. */
  columnVisibility?: DataTableColumnVisibility
  onColumnVisibilityChange?: (next: DataTableColumnVisibility) => void
  /** Slot before the search field, e.g. a selection count. */
  leading?: ReactNode
  /** Slot before the column manager, e.g. a row count. */
  trailing?: ReactNode
  className?: string
}

/**
 * The bar that goes with a `DataTable`: one search field, one button, and a
 * chip for every filter that is currently doing something.
 *
 * ```
 * [ search…              ]  [ filters 3 ▾ ]   waiting ×   plexor ×   planner ×
 * ```
 *
 * ## Why the row is shaped this way
 *
 * The bar used to render one control per declared filter, side by side. On the
 * runs board that is five controls before the operator has read a single row,
 * and on a narrow window they wrapped into a second and third line of chrome
 * above a list that had lost the space to show itself. So the controls fold
 * into a popover and only their *readings* stay on the row.
 *
 * - **The search field and the button are the fixed left edge.** They are the
 *   two things the operator aims at, and a control that moves when a filter
 *   goes on is a control that has to be re-found on every use. Chips grow to
 *   the right of them and wrap; nothing to the left of a chip ever moves.
 * - **The count answers *how many*, the chips answer *which*.** They are the
 *   same set counted two ways, so they can never disagree.
 * - **A chip removes exactly its own filter**, and the row renders no chip
 *   list at all when nothing is active — an empty strip would reserve height
 *   for a reading that is not there.
 *
 * ## Which control is the search
 *
 * Filters are still declared once, on the column they belong to. The first
 * `text` filter a column set declares is promoted to the row's search field —
 * screens already write one text filter that matches across several fields
 * (`runs-columns.tsx` matches run id, task, app and step from a single box),
 * and that *is* the search. Promoting it beats adding a second search box
 * beside it that would filter the same rows by a different rule.
 *
 * Everything else — every `select`, and any further `text` filter — goes in
 * the popover. The promoted filter gets no chip and is not counted: its value
 * is already legible in the field it is typed into, and a chip repeating it
 * would be the only chip that does not correspond to something hidden.
 *
 * A screen that declares no `text` filter gets no search field, and a screen
 * whose only filter is that text filter gets no button. Neither is a special
 * case in here; both fall out of the same rule.
 *
 * ## The controls are the kit's
 *
 * The filter select used to be built here, out of React Aria parts, because
 * `shared/ui` had no select to give it and a form's native `<select>` was the
 * wrong shape for a dense bar. That control is now `shared/ui/select`, and the
 * three things this bar needs that a form does not — a 1.5rem height, a row
 * that clears the filter, and the mark a control wears while it is narrowing
 * something — are props on it rather than a second component. A form and a
 * toolbar on the same screen now wear the same control.
 *
 * ## Where the bar lives
 *
 * Kept a sibling of the table rather than a slot inside it, so the table stays
 * a layout primitive and this bar can carry its own chrome. Being a sibling is
 * also what lets it leave: a screen inside `AppShell` hands this to
 * `PageHeader`'s `filters` slot instead of stacking it on the table, and the
 * controls that decide which rows the screen is showing then sit in the one
 * band that never scrolls. Nothing about this component changes — the screen
 * still owns `filters` and `columnVisibility`, and the same values still reach
 * the same table. `runs-page.tsx` is the worked example; the contract is
 * written out on `PageHeader`.
 */
export function DataTableToolbar<TData extends RowData>({
  columns,
  filters,
  onFiltersChange,
  columnVisibility,
  onColumnVisibilityChange,
  leading,
  trailing,
  className,
}: DataTableToolbarProps<TData>) {
  const specs = dataFilterSpecs(columns)
  // First `text` filter declared wins the row. See the note above.
  const searchIndex = specs.findIndex((spec) => spec.filter.kind === "text")
  const search = searchIndex === -1 ? undefined : specs[searchIndex]
  const sheet = specs.filter((_, index) => index !== searchIndex)

  // One derivation, two readings: the number on the button and the chips are
  // the same list, so the count can never claim a filter the chips do not show.
  const chips = sheet
    .map((spec) => ({ spec, value: filters[spec.id] ?? "" }))
    .filter((entry) => entry.value !== "")

  const active = hasActiveFilters(filters)
  const showColumns =
    columnVisibility !== undefined && !!onColumnVisibilityChange

  const update = (id: string, next: string) => {
    onFiltersChange({ ...filters, [id]: next })
  }

  const reset = () => {
    const cleared: DataTableFilterValues = {}
    for (const spec of specs) {
      cleared[spec.id] = ""
    }
    onFiltersChange(cleared)
  }

  return (
    <div className={cn(styles.bar, className)} data-test="data-table-toolbar">
      {/* The fixed left edge: whatever the screen puts in front, then the two
          controls the operator aims at. Never reordered, never displaced. */}
      <div className={styles.controls}>
        {leading}
        {search ? (
          <SearchField
            spec={search}
            value={filters[search.id] ?? ""}
            onChange={(next) => {
              update(search.id, next)
            }}
          />
        ) : null}
        {sheet.length > 0 ? (
          <FilterSheet
            specs={sheet}
            filters={filters}
            count={chips.length}
            resettable={active}
            onChange={update}
            onReset={reset}
          />
        ) : null}
      </div>

      {chips.length > 0 ? (
        <ul className={styles.chips} data-test="data-table-chips">
          {chips.map(({ spec, value }) => (
            <li key={spec.id} className={styles.chipItem}>
              <FilterChip
                spec={spec}
                value={value}
                onRemove={() => {
                  update(spec.id, "")
                }}
              />
            </li>
          ))}
        </ul>
      ) : null}

      <div className={styles.tail}>
        {trailing}
        {showColumns ? (
          <ColumnManager
            columns={columns}
            visibility={columnVisibility}
            onChange={onColumnVisibilityChange}
          />
        ) : null}
      </div>
    </div>
  )
}

/** How a value spells on a chip: a select shows its option's own words. */
function valueLabel<TData extends RowData>(
  filter: DataColumnFilter<TData>,
  value: string
): string {
  if (filter.kind !== "select") {
    return value
  }
  return filter.options.find((option) => option.value === value)?.label ?? value
}

interface SearchFieldProps<TData extends RowData> {
  spec: DataFilterSpec<TData>
  value: string
  onChange: (next: string) => void
}

/**
 * The promoted text filter. A real `type="search"` so the browser gives it the
 * clearing affordance and assistive tech announces it as a search rather than
 * as one more text box.
 */
function SearchField<TData extends RowData>({
  spec,
  value,
  onChange,
}: SearchFieldProps<TData>) {
  return (
    <div className={styles.search}>
      <Search className={styles.searchIcon} aria-hidden="true" />
      <input
        type="search"
        className={styles.input}
        data-test="data-table-search"
        aria-label={`Filter by ${spec.label}`}
        placeholder={spec.filter.placeholder ?? `search ${spec.label}…`}
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
        }}
      />
    </div>
  )
}

interface FilterSheetProps<TData extends RowData> {
  specs: DataFilterSpec<TData>[]
  filters: DataTableFilterValues
  count: number
  resettable: boolean
  onChange: (id: string, next: string) => void
  onReset: () => void
}

/**
 * Every filter the screen's columns declare, bar the promoted search, laid out
 * two to a line. The trigger carries the count because that is the one thing
 * the chips beside it cannot say in less space than they take.
 */
function FilterSheet<TData extends RowData>({
  specs,
  filters,
  count,
  resettable,
  onChange,
  onReset,
}: FilterSheetProps<TData>) {
  return (
    <DialogTrigger>
      <AriaButton
        className={styles.sheetTrigger}
        data-test="data-table-filters"
        data-active={count === 0 ? undefined : ""}
        aria-label={count === 0 ? "Filters" : `Filters, ${count} active`}
      >
        <Filter className={styles.icon} aria-hidden="true" />
        <span>filters</span>
        {count > 0 ? (
          <span className={styles.count} aria-hidden="true">
            {count}
          </span>
        ) : null}
        <ChevronDown className={styles.icon} aria-hidden="true" />
      </AriaButton>
      {/* The column count rides on the popover, not on the grid inside it, and
          that placement is load-bearing rather than tidy: an overlay is
          absolutely positioned, so with no width of its own it shrink-wraps its
          content — and a `1fr` track has nothing to distribute inside a box
          whose width is itself derived from that content, so the fields came
          out ragged and cramped instead of even. The sheet therefore takes a
          definite measure computed from this number, and the grid inherits the
          same number to lay itself out against. Two readings of one value. */}
      <Popover
        className={styles.sheetPopover}
        placement="bottom start"
        style={
          {
            "--sheet-cols": Math.min(specs.length, SHEET_COLUMNS),
          } as CSSProperties
        }
      >
        <Dialog className={styles.sheet} aria-label="Filters">
          <div className={styles.fields}>
            {specs.map((spec) => (
              <SheetField
                key={spec.id}
                spec={spec}
                value={filters[spec.id] ?? ""}
                onChange={(next) => {
                  onChange(spec.id, next)
                }}
              />
            ))}
          </div>
          {resettable ? (
            <div className={styles.sheetFoot}>
              <Tooltip content="Clear all filters">
                <Button
                  variant="ghost"
                  size="icon-sm"
                  data-test="data-table-reset-filters"
                  aria-label="Clear all filters"
                  onClick={onReset}
                >
                  <FilterX aria-hidden="true" />
                </Button>
              </Tooltip>
            </div>
          ) : null}
        </Dialog>
      </Popover>
    </DialogTrigger>
  )
}

interface SheetFieldProps<TData extends RowData> {
  spec: DataFilterSpec<TData>
  value: string
  onChange: (next: string) => void
}

/** One named control in the sheet. The name is above the control, not beside
 *  it, so a long column label never squeezes the control it belongs to. */
function SheetField<TData extends RowData>({
  spec,
  value,
  onChange,
}: SheetFieldProps<TData>) {
  const inputId = useId()
  const labelId = `${inputId}-label`

  if (spec.filter.kind === "select") {
    return (
      <div className={styles.field}>
        <label className={styles.fieldLabel} id={labelId} htmlFor={inputId}>
          {spec.label}
        </label>
        {/* The kit's select, with the three things a filter needs said as
            props rather than as a second component: `size` for the bar's
            density, `clearable` for the row that puts the filter back to
            "all", and `active` for the mark a control wears while it is
            narrowing something. `placeholder` is read twice on purpose —
            it names the cleared state *and* the row that returns to it. */}
        <Select
          id={inputId}
          size="sm"
          clearable
          value={value}
          onValueChange={onChange}
          options={spec.filter.options}
          placeholder={
            spec.filter.placeholder ?? `all ${spec.label.toLowerCase()}`
          }
          active={value !== ""}
          aria-labelledby={labelId}
          data-test={`data-table-filter-${spec.id}`}
        />
      </div>
    )
  }

  return (
    <div className={styles.field}>
      <label className={styles.fieldLabel} htmlFor={inputId}>
        {spec.label}
      </label>
      <input
        id={inputId}
        type="text"
        className={cn(styles.input, styles.sheetInput)}
        data-test={`data-table-filter-${spec.id}`}
        data-active={value === "" ? undefined : ""}
        placeholder={spec.filter.placeholder ?? `filter ${spec.label}…`}
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
        }}
      />
    </div>
  )
}

interface FilterChipProps<TData extends RowData> {
  spec: DataFilterSpec<TData>
  value: string
  onRemove: () => void
}

/**
 * One active filter, said out loud on the row. Hairline-bounded and square to
 * the sm step — a reading in the chrome's own material, not a coloured pill.
 *
 * The chip *is* the remove control: one chip, one filter, one target. The
 * visible word is the value, because that is the reading the operator came for
 * and the six statuses, the app keys and the profile names are distinct
 * vocabularies in this product. Which filter it belongs to is carried by the
 * accessible name and by the pointer's own tooltip, so "×" is never the whole
 * name of anything.
 */
function FilterChip<TData extends RowData>({
  spec,
  value,
  onRemove,
}: FilterChipProps<TData>) {
  const text = valueLabel(spec.filter, value)
  return (
    <button
      type="button"
      className={styles.chip}
      data-test={`data-table-chip-${spec.id}`}
      title={`Clear the ${spec.label} filter`}
      aria-label={`Clear the ${spec.label} filter: ${text}`}
      onClick={onRemove}
    >
      <span className={styles.chipText}>{text}</span>
      <X className={styles.chipIcon} aria-hidden="true" />
    </button>
  )
}

interface ColumnManagerProps<TData extends RowData> {
  columns: DataColumn<TData>[]
  visibility: DataTableColumnVisibility
  onChange: (next: DataTableColumnVisibility) => void
}

/**
 * Column visibility. The screen still owns the map — this only proposes the
 * next one. The first column is pinned: a table whose leading identity column
 * can be hidden leaves rows no way to identify themselves.
 *
 * It sits at the tail, apart from the filter row, because "which columns
 * exist" is not "which rows show" — it changes what a row spells, never which
 * rows there are, so it earns no chip and is not in the count.
 */
function ColumnManager<TData extends RowData>({
  columns,
  visibility,
  onChange,
}: ColumnManagerProps<TData>) {
  const entries = columns.map((column, index) => ({
    id: dataColumnId(column),
    label: dataColumnLabel(column),
    pinned: index === 0,
  }))
  const hiddenCount = entries.filter(
    (entry) => visibility[entry.id] === false
  ).length

  return (
    <DialogTrigger>
      <AriaButton
        className={styles.columnsTrigger}
        data-test="data-table-columns"
        aria-label="Columns"
      >
        <SlidersHorizontal className={styles.icon} aria-hidden="true" />
        {hiddenCount > 0 ? (
          <span className={styles.count}>{entries.length - hiddenCount}</span>
        ) : null}
      </AriaButton>
      <Popover className={styles.popover} placement="bottom end">
        <Dialog className={styles.dialog} aria-label="Columns">
          <p className={styles.dialogTitle}>Columns</p>
          {entries.map((entry) => (
            <label key={entry.id} className={styles.columnRow}>
              <input
                type="checkbox"
                className={styles.check}
                data-test={`data-table-column-${entry.id}`}
                disabled={entry.pinned}
                checked={visibility[entry.id] !== false}
                onChange={(event) => {
                  onChange({ ...visibility, [entry.id]: event.target.checked })
                }}
              />
              <span className={styles.columnLabel}>{entry.label}</span>
            </label>
          ))}
        </Dialog>
      </Popover>
    </DialogTrigger>
  )
}
