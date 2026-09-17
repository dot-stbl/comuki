import type { ReactNode } from "react"

import type { BudgetHeat } from "@/domains/cost/model/cost"
import { Surface, type SurfaceTone } from "@/shared/ui"

import styles from "./cost-stat.module.css"

export interface CostStatProps {
  /**
   * Which reading this is, for anything traversing the DOM.
   *
   * A separate attribute rather than an overridable `data-test`, the way
   * `capacity-card` names its pool: a test that wants "all three tiles" and a
   * test that wants "the proxy tile" are different questions, and an id that
   * replaces the component's own name can only answer the second.
   */
  name: string
  /** What the figure is — a data label, in the tight gesture. */
  label: string
  /**
   * The figure, already formatted.
   *
   * A string rather than a node, because this screen speaks three different
   * precisions on purpose — cents for a per-success price, whole dollars for a
   * day's total, whole percent for a cap — and each one is a decision the call
   * site has already made. A node here would let a caller put prose in the
   * data voice, which is the one defect the two-voices rule exists to catch.
   */
  value: string
  /** A currency mark riding before the figure. */
  prefix?: string
  /** A unit riding after it. */
  suffix?: string
  /** The line under the figure — prose, in the interface voice. */
  sub: ReactNode
  /**
   * How close this reading is to the thing it is capped by.
   *
   * Only the budget tile has one: it is the only tile on the screen whose
   * figure has a consequence written beside it. The other two are facts about
   * a period that has already happened, and a fact does not get a hue.
   */
  heat?: BudgetHeat
  /** A meter drawn between the figure and its line. */
  children?: ReactNode
  className?: string
}

/** The edge the reading lights, when the reading has a consequence. */
const TONE: Record<BudgetHeat, SurfaceTone> = {
  ok: "neutral",
  near: "attention",
  over: "danger",
}

/**
 * One reading off the day's report: what it is, what it is, and what that means.
 *
 * The screen's tile, and all three of them: the period total, the forecast and
 * the budget are one row of equal readings, and each of them used to be its own
 * file spelling out the same eight declarations — hairline on the start edge,
 * lane material, surface corner — under three different class names. Two of
 * those stylesheets are gone and the third keeps only what is genuinely its
 * own.
 *
 * The bounding is the kit's `Surface`, deliberately not a card and deliberately
 * not named one: what a card is made of is the fill that lifts it off the floor
 * and the shadow that floats it, and neither is here. `Surface` forwards
 * `data-test` and nothing else, on purpose, so the two finer hooks sit on the
 * elements they were always about — which reading this is, on the line that
 * names it; how hot the reading is, on the figure it is a reading of.
 *
 * The figure is the reading and anything drawn under it is decoration on top of
 * a reading already stated in words — which is why the meter slot sits between
 * the figure and the line that says what the figure is out of. Nothing on this
 * tile is announced only as a length.
 */
export function CostStat({
  name,
  label,
  value,
  prefix,
  suffix,
  sub,
  heat,
  children,
  className,
}: CostStatProps) {
  return (
    <Surface
      as="article"
      bound="start"
      tone={heat ? TONE[heat] : "neutral"}
      className={className}
      data-test="cost-stat"
    >
      <span className={styles.label} data-stat={name}>
        {label}
      </span>
      <span className={styles.figure} data-heat={heat}>
        {prefix ? <span className={styles.unit}>{prefix}</span> : null}
        <span className={styles.value}>{value}</span>
        {suffix ? <span className={styles.unit}>{suffix}</span> : null}
      </span>
      {children}
      <p className={styles.sub}>{sub}</p>
    </Surface>
  )
}
