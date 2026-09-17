import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import { StatLabel } from "../stat-tile"

import styles from "./fact-list.module.css"

/**
 * How the pairs are arranged. Three, because three are real and each of them
 * was arrived at twice or was the only thing that fitted its box.
 *
 * - `rows` (default) — the name in a track, the value beside it, pairs stacked.
 *   The arrangement for a record's facts on a page. With `framed` it becomes
 *   the bounded, divided block a project's four facts are read as.
 * - `split` — name and value on one line, pushed to opposite edges. The sheet's
 *   arrangement: a narrow panel where a short column of values has to be
 *   scannable down the end edge.
 * - `stack` — the name above its value, pairs flowing into as many columns as
 *   the board has room for. For six or more facts that have no natural order.
 */
export type FactListLayout = "rows" | "split" | "stack"

/**
 * How much room the values are being given.
 *
 * `md` is a page; `sm` is a sheet. One prop rather than two, because the two
 * things it changes change for the same reason — see the stylesheet. This is
 * the axis the six copies genuinely disagreed on, and the disagreement was not
 * a disagreement: the two screens at the small step were both panels and the
 * three at the large step were both pages. The one exception was a detail page
 * reading a step small, which is drift rather than a decision.
 */
export type FactListSize = "sm" | "md"

export interface FactListProps {
  layout?: FactListLayout
  size?: FactListSize
  /**
   * Bounded on four sides and divided between rows. `rows` only — a frame
   * around a column of stacked pairs is a box around nothing, and a frame
   * around a sheet's band is the band drawn twice.
   */
  framed?: boolean
  /** `Fact` elements. */
  children: ReactNode
  className?: string
  "data-test"?: string
}

/**
 * Named values about one record.
 *
 * A `<dl>`, always, and this is the one primitive in the kit where the element
 * is the point rather than chrome: a pair of a name and a value *is* a
 * definition list, and the screens that drew it with two `<span>`s threw away
 * the only structure assistive tech could have walked. `Fact` renders the
 * `<dt>`/`<dd>` pair; nothing else may.
 *
 * Six screens built this independently. The name half came out identical in
 * every one — same family, size, weight, tracking, ink, in the same order — so
 * the name is not configurable here at all; it is `StatLabel`, the kit's voice
 * for anything that names a reading. What actually differed was the container
 * and the size of the value, and those are the two props.
 *
 * `run-detail-page` is deliberately not a caller: its `.facts` is a strip of
 * icon-led chips with no names in it at all — a `Hash` glyph is a recognition
 * cue, not a `<dt>` — and folding it in would mean a layout whose "name" is
 * allowed to be an icon, which is a different component wearing this one's
 * class names.
 */
export function FactList({
  layout = "rows",
  size = "md",
  framed = false,
  children,
  className,
  "data-test": dataTest,
}: FactListProps) {
  return (
    <dl
      className={cn(
        styles.list,
        styles[layout],
        size === "sm" && styles.sm,
        layout === "rows" && framed && styles.framed,
        className
      )}
      data-test={dataTest}
    >
      {children}
    </dl>
  )
}

export interface FactProps {
  /** What the value is called. Lower case, in the tight gesture. */
  name: ReactNode
  /**
   * Which voice the value takes.
   *
   * `data` (default) for anything the operator reads back somewhere else — a
   * slug, a host, a date, an id. `prose` for the one field on a record a human
   * wrote. Getting this backwards is the defect the two-voices rule exists to
   * catch, which is why it is a named prop rather than a class a call site
   * passes.
   */
  voice?: "data" | "prose"
  /**
   * Nothing is there, and the value says so in words — `never`, `local only`,
   * `platform defaults`. Faint rather than blank: a blank cell reads as a
   * broken render, and the operator cannot tell "we have no value" from "the
   * page failed to draw one".
   */
  absent?: boolean
  /**
   * Selected as a unit rather than as part of a sentence. For the one fact on
   * a screen somebody copies out of it — a repository url, a run id.
   */
  selectable?: boolean
  children: ReactNode
  /** Lands on the `<dd>`, which is the half a call site ever needs to reach. */
  className?: string
  "data-test"?: string
}

/**
 * One named value.
 *
 * The `<dd>` takes the call site's `className` and `data-test` because the
 * value is the half that varies: a status word that goes faint when the account
 * is off, a credential that carries a note under it, a row that holds the act
 * it is missing. The `<dt>` never varies — that was the finding.
 */
export function Fact({
  name,
  voice = "data",
  absent = false,
  selectable = false,
  children,
  className,
  "data-test": dataTest,
}: FactProps) {
  return (
    <div className={styles.fact}>
      <StatLabel as="dt" className={styles.name}>
        {name}
      </StatLabel>
      <dd
        className={cn(
          styles.value,
          voice === "prose" && styles.prose,
          absent && styles.absent,
          selectable && styles.selectable,
          className
        )}
        data-test={dataTest}
      >
        {children}
      </dd>
    </div>
  )
}
