import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./form.module.css"
import { fieldDescriptionId } from "./ids"

export interface SwitchFieldProps {
  id: string
  label: string
  checked: boolean
  onCheckedChange: (next: boolean) => void
  /** The state, in a word — `on` / `off`, or something the screen means more. */
  onLabel?: string
  offLabel?: string
  hint?: ReactNode
  /** Busy or structurally impossible. Never a permission denial. */
  disabled?: boolean
  /**
   * This role may not flip it — the sentence naming what would.
   *
   * `aria-disabled` and not `disabled`, for the same reason `Button` draws the
   * distinction: a disabled control fires no pointer events, so the tooltip
   * explaining it is unreachable by pointer and out of the tab order both.
   */
  denied?: string | null
  "data-test"?: string
}

/**
 * A switch, and the word that says which way it is pointing.
 *
 * Rectilinear rather than a pill, because `--r-pill` is a retired step in this
 * form language and a switch is not an exception to it. The reading is carried
 * by the thumb's position *and* by the word beside it, so it never depends on
 * the fill's hue alone — the same two-channel rule the status bands follow.
 */
export function SwitchField({
  id,
  label,
  checked,
  onCheckedChange,
  onLabel = "on",
  offLabel = "off",
  hint,
  disabled = false,
  denied,
  "data-test": dataTest,
}: SwitchFieldProps) {
  const blocked = Boolean(denied)

  return (
    <div className={styles.field}>
      <div className={styles.switchRow}>
        <span
          className={cn(
            styles.switch,
            checked && styles.switchOn,
            (disabled || blocked) && styles.switchOff
          )}
        >
          <input
            type="checkbox"
            role="switch"
            id={id}
            className={styles.switchInput}
            checked={checked}
            disabled={disabled}
            aria-disabled={blocked || undefined}
            title={denied ?? undefined}
            aria-describedby={hint ? fieldDescriptionId(id) : undefined}
            data-test={dataTest}
            onChange={(event) => {
              if (blocked) {
                return
              }
              onCheckedChange(event.target.checked)
            }}
          />
          <span className={styles.switchThumb} aria-hidden="true" />
        </span>
        <span className={styles.switchText}>
          <label className={styles.switchLabel} htmlFor={id}>
            {label}
          </label>
          <span className={styles.switchState} aria-hidden="true">
            {checked ? onLabel : offLabel}
          </span>
        </span>
      </div>
      {hint ? (
        <span className={styles.hint} id={fieldDescriptionId(id)}>
          {hint}
        </span>
      ) : null}
    </div>
  )
}
