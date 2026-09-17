import { cn } from "@/shared/lib/utils"

import styles from "./meter.module.css"

/**
 * The two geometries a channel is drawn at, and there is no third.
 *
 * - `tile` — `--h-meter` tall on the `--r-xs` corner. A bar that has a band of
 *   its own: a budget tile, a ranking row, a capacity card.
 * - `row` — two pixels on the hairline corner. A bar squeezed under the figures
 *   inside a one-line table cell, where the rows are uniform by contract and
 *   nothing may add height.
 *
 * Not a scale with a middle. The two are what the container dictates, and a
 * container is either a band or a table row.
 */
export type MeterTrack = "row" | "tile"

/**
 * Where the fill's ink comes from.
 *
 * - `neutral` — `--text-faint`, at every length. The refusal is the reading:
 *   saturation in this product is reserved for status inside the flow, and a
 *   spend ranking carries no status — nobody is being asked to do anything
 *   about being third.
 * - `heat` — the `--st` the call site has already set on an ancestor for the
 *   figures beside the bar, falling back to the neutral when it has set none.
 *
 * A token *name* is deliberately not a prop. Every call site drives this off a
 * state attribute (`[data-heat]`, `[data-binding]`) that also colours the
 * figures, so a prop would be a second authority on the same reading — and the
 * mapping would have to be restated in TypeScript beside the copy that already
 * lives in CSS. It would also be the wrong mechanism: a kit class and a domain
 * class are both one-class selectors, and which of two equal-specificity rules
 * wins is decided by CSS-module bundle order, which is not a contract. A custom
 * property read with a fallback has no race to lose.
 */
export type MeterTone = "neutral" | "heat"

/**
 * Which sentence the hatch is saying. Solid when absent.
 *
 * - `queued` — nothing will act on this number. A cap recorded but not
 *   enforced; a kill-switch already thrown, so the figure has stopped moving.
 * - `failed` — the thing at the end of the bar has already happened.
 *
 * The fraction stays true in both: the spend is real, and the length keeps
 * saying so. What the weave withdraws is the claim a smooth fill makes.
 */
export type MeterHatch = "queued" | "failed"

export interface MeterProps {
  /**
   * The fill's length as a fraction of the channel, clamped to 0…1.
   *
   * `null` is not zero: it is a channel with no reading behind it, drawn
   * hatched and with no fill, for a source that did not answer. An empty
   * channel would say "nothing used", which is the one thing that case is not
   * saying.
   */
  value: number | null
  track?: MeterTrack
  tone?: MeterTone
  /**
   * Draw the channel as a bordered box. For a meter standing on the page's own
   * surface, where the lane material alone is not a strong enough step to read
   * as an empty container; inside a tile or a cell the surrounding material
   * already does that work.
   */
  edge?: boolean
  hatched?: MeterHatch
  className?: string
  "data-test"?: string
}

const TRACK = {
  row: styles.row,
  tile: undefined,
} as const

const HATCH = {
  queued: styles.queued,
  failed: styles.failed,
} as const

/**
 * The fill's length in whole percent.
 *
 * Clamped before it is rounded rather than after, which is the one place the
 * five hand-written copies disagreed: `Math.min(100, Math.round(share * 100))`
 * holds for a share above one and quietly draws a negative width for a share
 * below zero. No `budgetShare` in the product returns a negative today — but a
 * bar is fed by arithmetic on figures the backend supplies, and the shape of
 * this guard should not depend on that staying true.
 */
function percent(value: number): number {
  if (!Number.isFinite(value)) {
    return 0
  }
  return Math.round(Math.min(1, Math.max(0, value)) * 100)
}

/**
 * A drawn empty channel with a fill measured against it. Nothing else.
 *
 * No figures, no label, no name. That is the whole of the concept the four
 * hand-built copies actually agreed on — `cost/ui/proxy-budget-meter`,
 * `cost/ui/ranked-table`, `compute/ui/capacity-card` and
 * `settings/ui/budget-meter` each wrote out the same channel and the same
 * absolutely-positioned fill, and then went their own ways completely over what
 * the bar sits next to. The figures stay where they are: a key's cap, a spend
 * ranking and a pool's two ceilings are three different readings, and only the
 * shape under them is one thing.
 *
 * ```tsx
 * <p className={styles.reading}>{percent}% · ${used} / ${cap}</p>
 * <Meter value={share} tone="heat" hatched={stopped ? "queued" : undefined} />
 * ```
 *
 * **It is always `aria-hidden`, and that is not a prop.** A channel with no
 * figures inside it can only ever be decoration on top of a reading somebody
 * else is stating, because a length is not a reading — which is the sentence
 * three of the four docblocks this replaces already spelled out for themselves.
 * Making the hiding optional would invite a screen to announce a bar and call
 * the job done. A surface where the bar genuinely *is* the reading needs a role
 * and a label and is a different component; it is not this one with a flag
 * flipped.
 *
 * The call site keeps its own `aria-hidden` wherever it had one on an ancestor
 * — `proxy-budget-meter` hides its whole root because the tile around it states
 * the number — and nesting a hidden element inside a hidden subtree costs
 * nothing.
 */
export function Meter({
  value,
  track = "tile",
  tone = "neutral",
  edge = false,
  hatched,
  className,
  "data-test": dataTest,
}: MeterProps) {
  return (
    <span
      className={cn(
        styles.channel,
        TRACK[track],
        edge && styles.edge,
        tone === "heat" && styles.heat,
        hatched && HATCH[hatched],
        value === null && styles.noReading,
        className
      )}
      aria-hidden="true"
      data-test={dataTest}
    >
      {value === null ? null : (
        <span
          className={styles.fill}
          style={{ inlineSize: `${percent(value)}%` }}
        />
      )}
    </span>
  )
}
