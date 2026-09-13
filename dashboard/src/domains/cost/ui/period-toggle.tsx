import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./period-toggle.module.css"

export type PeriodOption = "day" | "week" | "month"

export interface PeriodToggleProps {
  value: PeriodOption
  onChange: (next: PeriodOption) => void
  options: ReadonlyArray<{ value: PeriodOption; label: string; note?: string }>
  className?: string
  /** Right-hand annotation — the dashboard ships "vs previous: -3%" here. */
  trailing?: ReactNode
}

/**
 * A pill row that picks one of three period lengths.
 *
 * Built around a `<button>` per option (not a `<select>`) because three
 * values fit on a row and a dropdown makes the operator reach past the
 * reading they want to compare. The active value is the only one with the
 * pressed appearance; the others say in words what they would change.
 *
 * `aria-pressed` is the radial-group read for the screen reader; the role
 * itself is the implicit `group` from the wrapper.
 */
export function PeriodToggle({
  value,
  onChange,
  options,
  className,
  trailing,
}: PeriodToggleProps) {
  return (
    <div
      className={cn(styles.toggle, className)}
      role="group"
      aria-label="Period"
      data-test="period-toggle"
    >
      {options.map((option) => {
        const pressed = option.value === value
        return (
          <button
            key={option.value}
            type="button"
            className={cn(styles.option, pressed && styles.optionActive)}
            aria-pressed={pressed}
            data-test="period-toggle-option"
            data-value={option.value}
            onClick={() => onChange(option.value)}
          >
            <span className={styles.optionLabel}>{option.label}</span>
            {option.note ? (
              <span className={styles.optionNote}>{option.note}</span>
            ) : null}
          </button>
        )
      })}
      {trailing ? (
        <span className={styles.trailing}>{trailing}</span>
      ) : null}
    </div>
  )
}
