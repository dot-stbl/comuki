import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./form.module.css"
import { fieldDescriptionId } from "./ids"

export interface SwitchFieldProps {
  id: string
  label: string
  checked: boolean
  onCheckedChange: (next: boolean) => void
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
 * A switch on one row: the label at the start, the track at the end.
 *
 * Rectilinear rather than a pill, because `--r-pill` is a retired step in this
 * form language and a switch is not an exception to it. The reading is the
 * thumb's side and the track's fill — brand-washed when on, bare lane when
 * off — and, for a screen reader, the input's own checked state under
 * `role="switch"`. It once also said the state in a word beside the track;
 * the word made the control two lines tall and stretched every row it sat in,
 * and it said what the thumb was already saying.
 */
export function SwitchField({
  id,
  label,
  checked,
  onCheckedChange,
  hint,
  disabled = false,
  denied,
  "data-test": dataTest,
}: SwitchFieldProps) {
  const blocked = Boolean(denied)

  return (
    <div className={styles.field}>
      <div className={styles.switchRow}>
        <label className={styles.switchLabel} htmlFor={id}>
          {label}
        </label>
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
      </div>
      {hint ? (
        <span className={styles.hint} id={fieldDescriptionId(id)}>
          {hint}
        </span>
      ) : null}
    </div>
  )
}
