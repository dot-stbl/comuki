import type { ReactNode } from "react"

import { Button, Field } from "@/shared/ui"

import styles from "./color-field.module.css"

/** The swatch's own spelling of "no value chosen yet". A control value, not
 *  chrome — the stylesheet sees none of it; the moment the operator picks,
 *  their value replaces it, and the refusal puts the placeholder back. */
const UNSET_COLOR = "#8b8b96"

export interface ColorFieldProps {
  /** The control's id. The label points at it, so it is required. */
  id: string
  label: string
  /** The committed `#rrggbb`, or `null` for "not chosen / keep as stored". */
  value: string | null
  onValueChange: (next: string | null) => void
  /**
   * What the refusal beside the swatch is called. The same wire value means
   * two different things on the two surfaces that collect a colour: on a
   * create it is "no colour" (nothing is stored), on a patch it is "keep the
   * stored colour" (`null` leaves the field untouched). The component holds
   * the control; the screen owns the sentence.
   */
  clearLabel: string
  hint?: ReactNode
  disabled?: boolean
}

/**
 * The native colour picker, dressed in the kit's control material.
 *
 * A native `<input type="color">` always carries a value, so "none" needs a
 * second control beside it — a pressed-when-null refusal, in the surface's
 * own words. The colour it paints is the operator's data (the accent a
 * project's identity renders through), which is the one saturated thing a
 * form in this product is allowed to hold, held as a value and not as chrome.
 */
export function ColorField({
  id,
  label,
  value,
  onValueChange,
  clearLabel,
  hint,
  disabled = false,
}: ColorFieldProps) {
  return (
    <Field id={id} label={label} hint={hint}>
      <div className={styles.row}>
        <input
          type="color"
          id={id}
          className={styles.input}
          value={value ?? UNSET_COLOR}
          disabled={disabled}
          data-test="color-field-input"
          aria-label={label}
          onChange={(event) => {
            onValueChange(event.target.value)
          }}
        />
        <Button
          type="button"
          variant="secondary"
          size="sm"
          data-test="color-field-clear"
          aria-pressed={value === null}
          disabled={disabled}
          onClick={() => {
            onValueChange(null)
          }}
        >
          {clearLabel}
        </Button>
      </div>
    </Field>
  )
}
