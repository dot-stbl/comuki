import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./ranked-table.module.css"

export interface RankedColumn {
  /** The column's name, in the head. */
  label: string
  /**
   * The figure the ranking is actually by — drawn at full strength.
   *
   * The rest are context beside it and stay muted, so a scan down the table
   * lands on one column rather than on three competing ones.
   */
  strong?: boolean
  /**
   * The cell truncates a bigger reading into its `title`, so the pointer says
   * there is more here than the column shows.
   */
  help?: boolean
}

export interface RankedFigure {
  /** The figure, already formatted — this screen speaks three precisions. */
  value: ReactNode
  /** What the column could not fit, on hover. */
  title?: string
  "data-test"?: string
}

export interface RankedRow {
  /** The row's identity — its React key, and the mark a test finds it by. */
  id: string
  /** What the row is: a name, or a link to wherever it lives. */
  label: ReactNode
  /** This row's length on the axis every row in the table shares, 0–1. */
  share: number
  /** One per column, in the columns' order. */
  figures: readonly RankedFigure[]
}

export interface RankedTableProps {
  /** The head of the name column — what the rows are. */
  label: string
  columns: readonly RankedColumn[]
  rows: readonly RankedRow[]
  /** What to say when the ranking is empty — one line, in the data voice. */
  empty: ReactNode
  /** The hook on the table itself. */
  "data-test"?: string
  /** The hook every row carries, so a test can count them. */
  rowTest?: string
  /** The hook on the empty line, which is a different answer from an empty table. */
  emptyTest?: string
  className?: string
}

/**
 * A ranking: what it is, how far along the shared axis it sits, and the figures.
 *
 * The cost report asks the same question in four places — which app, which
 * model, which project is expensive, and by how much against the one above it —
 * and for a while it answered in three different constructions: one semantic
 * `<table>` and two `<ul>` / `<li>` grids that were nearly but not quite each
 * other. This is the one construction.
 *
 * A table rather than a list, because both axes of these blocks carry meaning.
 * Down a column is the comparison the operator came for; across a row is one
 * record — a name, a share, a dollar figure, sometimes a token volume — and a
 * `<ul>` whose `<li>` is a grid with fixed tracks is a table that has given up
 * its column names. Giving them back fixes a real reading and not only the
 * markup: the model breakdown carries *two* numeric columns, and a head is the
 * only thing that says which is the tokens and which is the money.
 *
 * The bar is not a column. It rides in the name's own cell, measured against
 * the axis every row shares, because two bars on different scales cannot be
 * compared and comparing them is the whole task. It is decoration on top of a
 * figure the row already states in words, so it is out of the a11y tree
 * entirely.
 *
 * Neutral at every length. Saturation in this product is reserved for status
 * inside the flow, and a spend ranking carries no status — nobody is being
 * asked to do anything about being third. Length is what ranks, and length
 * survives greyscale.
 */
export function RankedTable({
  label,
  columns,
  rows,
  empty,
  "data-test": dataTest,
  rowTest,
  emptyTest,
  className,
}: RankedTableProps) {
  if (rows.length === 0) {
    return (
      <p className={cn(styles.empty, className)} data-test={emptyTest}>
        {empty}
      </p>
    )
  }

  return (
    <table className={cn(styles.table, className)} data-test={dataTest}>
      <thead>
        <tr>
          <th scope="col" className={styles.headLabel}>
            {label}
          </th>
          {columns.map((column) => (
            <th key={column.label} scope="col" className={styles.headFigure}>
              {column.label}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr
            key={row.id}
            className={styles.row}
            data-test={rowTest}
            data-id={row.id}
          >
            <td className={styles.labelCell}>
              {/* The grid is inside the cell rather than on it: a `<td>` that
                  declares `display: grid` stops being a table cell, and the
                  table then has to generate an anonymous one around it. */}
              <span className={styles.labelBox}>
                <span className={styles.name}>{row.label}</span>
                <span className={styles.channel} aria-hidden="true">
                  <span
                    className={styles.fill}
                    style={{
                      inlineSize: `${Math.round(Math.min(1, Math.max(0, row.share)) * 100)}%`,
                    }}
                  />
                </span>
              </span>
            </td>
            {row.figures.map((figure, index) => {
              const column = columns[index]
              return (
                <td
                  key={column?.label ?? index}
                  className={cn(
                    styles.figure,
                    column?.strong && styles.strong,
                    column?.help && styles.help
                  )}
                  title={figure.title}
                  data-test={figure["data-test"]}
                >
                  {figure.value}
                </td>
              )
            })}
          </tr>
        ))}
      </tbody>
    </table>
  )
}
