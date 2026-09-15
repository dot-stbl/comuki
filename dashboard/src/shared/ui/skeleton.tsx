import { cn } from "@/shared/lib/utils"

import styles from "./skeleton.module.css"

/**
 * How much inline room the block pays for itself — the same four steps
 * `ScreenState` takes, so the loading state and the state that replaces it sit
 * on the same edge and nothing shifts sideways when the data lands.
 */
export type SkeletonInset = "flush" | "gutter" | "page" | "none"

export interface SkeletonProps {
  /**
   * The bars.
   *
   * A number asks for that many at the kit's own rhythm. An array is the widths
   * themselves, one entry per bar, in any CSS inline size — which is what the
   * eighteen screens that drew this by hand were already passing, as a
   * `SKELETON_WIDTHS` constant beside the component.
   *
   * Uneven on purpose: a column of equal bars reads as a table that has
   * finished loading and is full of blanks.
   */
  lines?: number | readonly string[]
  inset?: SkeletonInset
  /**
   * Grow into the space the loaded content will take, instead of being as tall
   * as its own bars. For a block standing in a flex column that owns the rest
   * of the screen — a table body, a board.
   */
  fill?: boolean
  /**
   * What is loading, said once for a screen reader. The bars themselves are
   * decoration and announce nothing.
   */
  label?: string
  className?: string
  "data-test"?: string
}

/**
 * The kit's rhythm, cycled when a call site asks for a count rather than for
 * widths. Nine entries because the longest skeleton in the product is nine
 * bars, so no screen ever sees the sequence repeat.
 */
const RHYTHM = [
  "58%",
  "82%",
  "44%",
  "70%",
  "52%",
  "76%",
  "38%",
  "64%",
  "48%",
] as const

const INSET = {
  flush: styles.flush,
  gutter: styles.gutter,
  page: styles.page,
  none: undefined,
} as const

function widthsOf(lines: number | readonly string[]): readonly string[] {
  if (typeof lines !== "number") return lines
  return Array.from(
    { length: Math.max(0, Math.trunc(lines)) },
    (_, index) => RHYTHM[index % RHYTHM.length] as string
  )
}

/**
 * Loading — the one of §17's four states that is not a sentence.
 *
 * The other three say what happened; this one says the answer is on its way by
 * standing where the answer will be. So it has no title and no prose: a
 * skeleton that explained itself would be a fifth state.
 *
 * Eighteen screens wrote this for themselves, and the surprising part is how
 * little they disagreed — the same `--h-meter` bar on the same `--r-xs` corner
 * in the same `--muted`, `--s2` apart, differing only in how many and how wide.
 * That is the whole parameterisation here. The four outliers that are genuinely
 * a different drawing — a board, a row of columns, a stack of cards — are not
 * pretending to be this, and are better off staying what they are than being
 * expressed as a bar count.
 *
 * ```tsx
 * const SKELETON_WIDTHS = ["58%", "42%", "71%", "50%"]
 *
 * {isLoading ? (
 *   <Skeleton lines={SKELETON_WIDTHS} inset="gutter" fill data-test="tasks-loading" />
 * ) : null}
 * ```
 */
export function Skeleton({
  lines = 4,
  inset = "flush",
  fill = false,
  label = "Loading",
  className,
  "data-test": dataTest,
}: SkeletonProps) {
  const widths = widthsOf(lines)

  return (
    <div
      className={cn(styles.stack, INSET[inset], fill && styles.fill, className)}
      /* `status` and not `alert`: a screen that is still loading has not
         interrupted anybody yet, and the polite live region is what announces
         "Loading" once without talking over whatever the operator was reading
         when they navigated here. */
      role="status"
      aria-label={label}
      data-test={dataTest}
    >
      {widths.map((width, index) => (
        <span
          key={index}
          className={styles.bar}
          style={{ inlineSize: width }}
          aria-hidden="true"
        />
      ))}
    </div>
  )
}
