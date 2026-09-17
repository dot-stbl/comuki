import type { InputHTMLAttributes, ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import { Field } from "./field"
import styles from "./form.module.css"
import { fieldDescriptionId } from "./ids"

export interface TextFieldProps extends Omit<
  InputHTMLAttributes<HTMLInputElement>,
  "id" | "value" | "onChange"
> {
  id: string
  label: string
  /** The label is real but not drawn — see `FieldProps.labelHidden`. */
  labelHidden?: boolean
  /**
   * The form will not go without this field — see `FieldProps.required`.
   *
   * It is taken off the input's own attribute set on purpose: this draws the
   * word in the label and sets `aria-required`, and never the native
   * `required` attribute, which would hand the browser a second, louder
   * validation story than the one the form already tells.
   */
  required?: boolean
  value: string
  onValueChange: (next: string) => void
  hint?: ReactNode
  error?: string | null
  /**
   * A small control riding inside the box, pinned to its end edge.
   *
   * For the one act that belongs to the value in the box rather than to the
   * form's footer — the probe button on a url field, so "the field and test
   * it" reads as one control. The number field's unit is the same device:
   * something outside the box would read as a second field, and something
   * absent reads as a question.
   */
  suffix?: ReactNode
}

/**
 * The plainest control there is: a label, a box, and the reason it is unhappy.
 *
 * `onValueChange` rather than `onChange` because every call site here wants the
 * string and none of them want the event — and because a form that reads
 * `event.target.value` in nine places is nine places to get it wrong.
 */
export function TextField({
  id,
  label,
  labelHidden,
  required,
  value,
  onValueChange,
  hint,
  error,
  suffix,
  ...rest
}: TextFieldProps) {
  return (
    <Field
      id={id}
      label={label}
      labelHidden={labelHidden}
      required={required}
      hint={hint}
      error={error}
    >
      {suffix ? (
        <span className={styles.controlBox}>
          <input
            {...rest}
            id={id}
            className={cn(styles.control, styles.controlWithSuffix)}
            value={value}
            aria-required={required ? true : undefined}
            aria-invalid={error ? true : undefined}
            aria-describedby={
              hint || error ? fieldDescriptionId(id) : undefined
            }
            onChange={(event) => onValueChange(event.target.value)}
          />
          <span className={styles.controlSuffix}>{suffix}</span>
        </span>
      ) : (
        <input
          {...rest}
          id={id}
          className={styles.control}
          value={value}
          aria-required={required ? true : undefined}
          aria-invalid={error ? true : undefined}
          aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
          onChange={(event) => onValueChange(event.target.value)}
        />
      )}
    </Field>
  )
}
