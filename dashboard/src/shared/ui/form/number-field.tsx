import type { InputHTMLAttributes, ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import { Field } from "./field"
import styles from "./form.module.css"
import { fieldDescriptionId } from "./ids"

export interface NumberFieldProps
  extends Omit<
    InputHTMLAttributes<HTMLInputElement>,
    "id" | "type" | "value" | "onChange"
  > {
  id: string
  label: string
  /** The label is real but not drawn — see `FieldProps.labelHidden`. */
  labelHidden?: boolean
  value: string
  onValueChange: (next: string) => void
  /**
   * What the number is counted in, drawn inside the box at its end edge —
   * `USD`, `ms`, `items`. A unit is a value, so it takes the data voice and
   * the faint ink of a placeholder; without it a bare box makes the operator
   * supply the unit from memory, and two fields on one form invite two
   * different memories.
   */
  unit: string
  hint?: ReactNode
  error?: string | null
}

/**
 * A number the operator sets as a policy, not one they scroll to.
 *
 * A native number input hands the act to the browser's spinner — a pair of
 * unlabelled six-pixel arrows that nobody can hit twice in the same place and
 * a scroll-wheel that changes a cap by accident on the way past. So the
 * stepper is removed in both vendor spellings and the field keeps only what
 * the platform is good at: `inputMode="decimal"` for the right keyboard, and
 * the numeric type for the constraint that nothing but a number parses. The
 * step, if a call site needs one, still arrives as a prop; it is a contract
 * for validation, not a control on the screen.
 *
 * The value is a string on the wire here for the same reason it is on
 * `TextField`: `z.coerce.number()` at the form edge is where text becomes a
 * number, and a field that converted first would eat the operator's half-
 * typed `"2."` on every keystroke.
 */
export function NumberField({
  id,
  label,
  labelHidden,
  value,
  onValueChange,
  unit,
  hint,
  error,
  className,
  ...rest
}: NumberFieldProps) {
  return (
    <Field id={id} label={label} labelHidden={labelHidden} hint={hint} error={error}>
      <span className={styles.numberBox}>
        <input
          {...rest}
          id={id}
          type="number"
          inputMode="decimal"
          className={cn(styles.control, styles.numberInput, className)}
          value={value}
          aria-invalid={error ? true : undefined}
          aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
          onChange={(event) => onValueChange(event.target.value)}
        />
        {/* The unit is decoration around a value the input already owns, so it
            is neither focusable nor announced — the label carries it when a
            screen reader needs it said aloud. */}
        <span className={styles.numberUnit} data-test="number-unit" aria-hidden="true">
          {unit}
        </span>
      </span>
    </Field>
  )
}
