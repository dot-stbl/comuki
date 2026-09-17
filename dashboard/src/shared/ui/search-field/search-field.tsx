import { Search, X } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import styles from "./search-field.module.css"

/**
 * The kit's search field. One control, one job — narrow a list of things
 * by what the operator types.
 *
 * One surface uses it today: `DataTableToolbar`, for the text filter it
 * promotes out of a column onto the row. That count used to read "two",
 * and both halves were wishes rather than facts — the toolbar carried a
 * private component of the same name, and the knowledge screen still
 * builds its own (`domains/knowledge/ui/knowledge-search.tsx`, whose own
 * comment calls the absence of this primitive "a gap in the kit" it did
 * not know had been filled). The toolbar is now a real call site; the
 * knowledge screen is the one left, and it is a domain's to make.
 *
 * Whoever the callers are, the shape is the same: a single field at the
 * top of a list, named through `aria-label` rather than a visible
 * `<label>` — a search is the operator's command, not a form value, so it
 * does not earn the field envelope.
 *
 * `data-active` is the only state the chrome reads: a non-empty value
 * says "this filter is doing something", and the rule wears a brand tint
 * the way the select's active trigger does. The same voice across the
 * two controls, the same reading across the product.
 *
 * `size` keeps the toolbar's denser height (`sm` = `--h-button-sm`, the
 * 24px floor and no lower) and the form's standard height (`md` =
 * `--h-button-lg`). A control that changes height by reading changes
 * its reading; this one does not.
 */
export interface SearchFieldProps {
  /** The current query. */
  value: string
  /** What the operator typed. `""` clears the filter. */
  onValueChange: (next: string) => void
  /**
   * The words on the field before anything has been typed. Optional —
   * the data table's toolbar supplies "search runs…" and the knowledge
   * search supplies "Search rules, docs, skills…".
   */
  placeholder?: string
  /**
   * The field's name. Required: a search is a control with no visible
   * label, so the screen reader has nothing else to point at.
   *
   * Toolbar: `"Filter by runs"`. Knowledge: `"Filter rules, docs and
   * skills"`. The name says *what the field narrows*, not *that it is a
   * search box* — a screen reader already announces "search" from the
   * input's role.
   */
  "aria-label": string
  /**
   * The control is currently doing something. Driven by the caller
   * (`true` when `value !== ""`) rather than by `:has-text` — keeps the
   * mark in the call site that knows whether the filter narrows anything,
   * not on the control that only knows what was typed.
   */
  "data-active"?: boolean
  /**
   * The height. `md` is the form's standard; `sm` is the toolbar's
   * density. No other values — the two are the product's two heights and
   * no other height is a search box.
   */
  size?: "sm" | "md"
  /** Busy or structurally impossible. Never a permission denial. */
  disabled?: boolean
  /** Test hook on the input itself. */
  "data-test"?: string
  /** Optional className applied to the field root. */
  className?: string
}

/**
 * The kit's search field. See {@link SearchFieldProps} for the props —
 * `aria-label` is required because the field has no visible `<label>`
 * and a search needs to announce itself.
 */
export function SearchField({
  value,
  onValueChange,
  placeholder,
  "aria-label": ariaLabel,
  "data-active": dataActive,
  size = "md",
  disabled = false,
  "data-test": dataTest,
  className,
}: SearchFieldProps) {
  const hasValue = value.length > 0

  return (
    <div className={cn(styles.field, size === "sm" && styles.sm, className)}>
      <Search className={styles.icon} aria-hidden="true" />
      <input
        type="search"
        className={styles.input}
        data-active={dataActive && hasValue ? "" : undefined}
        aria-label={ariaLabel}
        placeholder={placeholder}
        value={value}
        disabled={disabled}
        data-test={dataTest}
        onChange={(event) => {
          onValueChange(event.target.value)
        }}
      />
      {hasValue ? (
        <button
          type="button"
          className={styles.clear}
          aria-label="Clear search"
          data-test={dataTest ? `${dataTest}-clear` : undefined}
          disabled={disabled}
          onClick={() => {
            onValueChange("")
          }}
        >
          <X className={styles.clearIcon} aria-hidden="true" />
        </button>
      ) : null}
    </div>
  )
}
