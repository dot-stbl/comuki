import type { InputHTMLAttributes, ReactNode } from "react"

import { Field } from "./field"
import styles from "./form.module.css"
import { fieldDescriptionId } from "./ids"

export interface TextFieldProps
  extends Omit<
    InputHTMLAttributes<HTMLInputElement>,
    "id" | "value" | "onChange"
  > {
  id: string
  label: string
  /** The label is real but not drawn — see `FieldProps.labelHidden`. */
  labelHidden?: boolean
  value: string
  onValueChange: (next: string) => void
  hint?: ReactNode
  error?: string | null
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
  value,
  onValueChange,
  hint,
  error,
  ...rest
}: TextFieldProps) {
  return (
    <Field id={id} label={label} labelHidden={labelHidden} hint={hint} error={error}>
      <input
        {...rest}
        id={id}
        className={styles.control}
        value={value}
        aria-invalid={error ? true : undefined}
        aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
        onChange={(event) => onValueChange(event.target.value)}
      />
    </Field>
  )
}
