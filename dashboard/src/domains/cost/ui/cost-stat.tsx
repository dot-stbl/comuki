import type { ReactNode } from "react"

import type { BudgetHeat } from "@/domains/cost/model/cost"
import { StatTile, type SurfaceTone } from "@/shared/ui"

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

/**
 * The cost report's word for a tone.
 *
 * The map is the whole of what is left of this component that the kit does not
 * own: `near` and `over` are readings about a budget, and a tile has no idea
 * what a budget is. This is the one place the domain's vocabulary meets the
 * kit's, which is exactly where a translation belongs.
 */
const TONE: Record<BudgetHeat, SurfaceTone> = {
  ok: "neutral",
  near: "attention",
  over: "danger",
}

/**
 * One reading off the day's report: what it is, what it is, and what that means.
 *
 * Now the kit's `StatTile` with this screen's vocabulary in front of it, and
 * nothing else — no stylesheet of its own at all. It used to carry the tile's
 * contents itself, and its own comment recorded that two sibling files had
 * already been folded into it for spelling the same declarations; what that
 * comment did not know is that the recipe had been arrived at independently
 * outside this domain as well, on a capacity card and on a knowledge screen.
 * Three domains is not a domain component.
 *
 * What stays here is the name, the three-word heat vocabulary and the map from
 * it — a `BudgetHeat` is a fact about a cap, and a primitive that knew what a
 * cap was would be a cost report with a stylesheet.
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
    <StatTile
      name={name}
      label={label}
      value={value}
      prefix={prefix}
      suffix={suffix}
      sub={sub}
      tone={heat ? TONE[heat] : "neutral"}
      className={className}
      data-test="cost-stat"
    >
      {children}
    </StatTile>
  )
}
