import type { KeyboardEvent, MouseEvent } from "react"

import type { AnomalyFlag } from "@/domains/runs/model/types"
import { ANOMALY_MULTIPLIER } from "@/domains/runs/model/anomaly"
import { Tooltip } from "@/shared/ui"
import { cn } from "@/shared/lib/utils"

import styles from "./anomaly-badge.module.css"

export interface AnomalyBadgeProps {
  flag: AnomalyFlag
  /** Open the breakdown dialog — fired on click and on Enter / Space. */
  onActivate?: () => void
  /**
   * Stable test handle, in case a row carries two badges side-by-side.
   * Defaults to `anomaly-badge`.
   */
  "data-test"?: string
  className?: string
}

/**
 * The badge a runaway run wears in the duty list.
 *
 * The icon and the multiplier are the two things a glance catches. The
 * hover surfaces the project's own median so the operator sees the rule
 * applied — `3×` is the threshold the project's own data triggered,
 * not an absolute bar — and the modal opens on click for the cost
 * breakdown.
 *
 * `role="button"` and the inline `onActivate` are deliberate: the badge
 * slots into a `<td>` cell which does not accept a `<button>` inside a
 * TanStack `<button>` reliably, and the screen reader still announces
 * it as an actionable thing. Keyboard parity (Enter, Space) is wired
 * here rather than in the cell so the badge is its own contract.
 */
export function AnomalyBadge({
  flag,
  onActivate,
  "data-test": dataTest = "anomaly-badge",
  className,
}: AnomalyBadgeProps) {
  const label = `${flag.multiplier}× median`
  const tip = `Cost ${flag.multiplier}× this project's median of $${flag.medianCost.toFixed(2)} — exceeds the ${ANOMALY_MULTIPLIER}× threshold. Click for breakdown.`

  function handleClick(event: MouseEvent<HTMLSpanElement>) {
    if (!onActivate) {
      return
    }
    event.stopPropagation()
    onActivate()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLSpanElement>) {
    if (!onActivate) {
      return
    }
    if (event.key !== "Enter" && event.key !== " ") {
      return
    }
    event.preventDefault()
    event.stopPropagation()
    onActivate()
  }

  return (
    <Tooltip content={tip}>
      <span
        role="button"
        tabIndex={onActivate ? 0 : -1}
        aria-label={`Cost anomaly — ${label}. Open breakdown.`}
        className={cn(styles.badge, className)}
        data-test={dataTest}
        data-multiplier={flag.multiplier}
        onClick={handleClick}
        onKeyDown={handleKeyDown}
      >
        <span className={styles.icon} aria-hidden="true">⚠</span>
        <span className={styles.label}>{label}</span>
      </span>
    </Tooltip>
  )
}
