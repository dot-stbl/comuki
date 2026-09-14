import type { ReactNode } from "react"
import { AlertTriangle } from "lucide-react"

import styles from "./form.module.css"
import { fieldDescriptionId, fieldLabelId } from "./ids"

export interface FieldProps {
  /** The control's id. The label points at it, so it is required. */
  id: string
  label: string
  /**
   * The label is real but not drawn. The sentence around the control names it
   * already — a row that reads "lead → [model]" has one label, not two — so
   * the element stays for the screen reader and the pointer target and nothing
   * is painted.
   */
  labelHidden?: boolean
  /** The rule the operator cannot see by looking at the box. */
  hint?: ReactNode
  /** What is wrong, in a sentence. Replaces the hint while it is present. */
  error?: string | null
  children: ReactNode
}

/**
 * The field-label voice, exported for the controls that are not `Field`.
 *
 * A radio row drawn as cards or segments still has a label above it and a
 * rule under it, and both must read exactly like every stacked field's — same
 * family, size, weight, tracking, colour — or the run from one label to its
 * control measures differently from the run from the next. That voice was
 * re-spelled in every such component until this export; now the voice lives
 * here, once, with the field it came from.
 */
export function FieldLabel({ id, children }: { id?: string; children: ReactNode }) {
  return (
    <span className={styles.label} id={id}>
      {children}
    </span>
  )
}

/**
 * The field-hint voice — the rule the operator cannot see by looking at the
 * control — exported beside `FieldLabel` for the same reason.
 */
export function FieldHint({ id, children }: { id?: string; children: ReactNode }) {
  return (
    <span className={styles.hint} id={id}>
      {children}
    </span>
  )
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
export function Field({
  id,
  label,
  labelHidden = false,
  hint,
  error,
  children,
}: FieldProps) {
  return (
    <div className={styles.field}>
      <label
        className={labelHidden ? styles.labelHidden : styles.label}
        id={fieldLabelId(id)}
        htmlFor={id}
      >
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
