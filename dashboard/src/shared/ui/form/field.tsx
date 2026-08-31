import type { ReactNode } from "react"
import { AlertTriangle } from "lucide-react"

import styles from "./form.module.css"
import { fieldDescriptionId, fieldLabelId } from "./ids"

export interface FieldProps {
  /** The control's id. The label points at it, so it is required. */
  id: string
  label: string
  /** The rule the operator cannot see by looking at the box. */
  hint?: ReactNode
  /** What is wrong, in a sentence. Replaces the hint while it is present. */
  error?: string | null
  children: ReactNode
}

/**
 * A label, a control, and one line under it.
 *
 * The error replaces the hint rather than stacking under it: a field that grows
 * a line when it goes wrong shifts every field beneath it, and in a dialog that
 * moves the submit button out from under the pointer about to press it. One
 * slot, one line, the same box either way.
 *
 * The message is never carried by colour alone — it takes a mark and a
 * sentence, so it survives greyscale exactly like a status band does.
 */
export function Field({ id, label, hint, error, children }: FieldProps) {
  return (
    <div className={styles.field}>
      <label className={styles.label} id={fieldLabelId(id)} htmlFor={id}>
        {label}
      </label>
      {children}
      {error ? (
        <span
          className={styles.error}
          id={fieldDescriptionId(id)}
          role="alert"
          data-test="field-error"
        >
          <AlertTriangle className={styles.errorIcon} aria-hidden="true" />
          {error}
        </span>
      ) : hint ? (
        <span className={styles.hint} id={fieldDescriptionId(id)}>
          {hint}
        </span>
      ) : null}
    </div>
  )
}
