import type { ReactNode } from "react"

import type { BudgetHeat } from "@/domains/cost/model/cost"
import { cn } from "@/shared/lib/utils"

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
   * Only the proxy budget has one: it is the only tile on the screen whose
   * figure has a consequence written beside it. The other two are facts about
   * a day that has already happened, and a fact does not get a hue.
   */
  heat?: BudgetHeat
  /** A meter drawn between the figure and its line. */
  children?: ReactNode
  className?: string
}

/**
 * One reading off the day's report: what it is, what it is, and what that means.
 *
 * Deliberately not a card, and deliberately not named one — the file it
 * replaced was `stat-card.tsx`, which is the forbidden thing said out loud. A
 * data surface here is bounded by a hairline and takes the corner its size
 * deserves; what it never takes is a fill that lifts it off the floor and a
 * shadow that floats it.
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
    <article
      className={cn(styles.stat, className)}
      data-test="cost-stat"
      data-stat={name}
      data-heat={heat}
    >
      <span className={styles.label}>{label}</span>
      <span className={styles.figure}>
        {prefix ? <span className={styles.unit}>{prefix}</span> : null}
        <span className={styles.value}>{value}</span>
        {suffix ? <span className={styles.unit}>{suffix}</span> : null}
      </span>
      {children}
      <p className={styles.sub}>{sub}</p>
    </article>
  )
}
