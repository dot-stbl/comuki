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
  /**
   * The form will not go without this field.
   *
   * A signal, not a rule: validation stays where it already is, at the form's
   * edge. This only makes the fact legible before the operator finds it out by
   * being refused.
   */
  required?: boolean
  /** The rule the operator cannot see by looking at the box. */
  hint?: ReactNode
  /** What is wrong, in a sentence. Replaces the hint while it is present. */
  error?: string | null
  children: ReactNode
}

/**
 * "required", said inside the label.
 *
 * Three decisions here, and two of them were bought the expensive way.
 *
 * It is a **word** rather than an asterisk, because a star is a convention a
 * form has to explain somewhere and this product has nowhere to explain it.
 *
 * It is **not** hidden from assistive tech, and it must not become hidden.
 * React Aria composes a select trigger's accessible name from its own value
 * node, which *replaces* the label association and drops `aria-required` on
 * the way — so on `SelectField` the accessible name is the only channel this
 * fact has. A marker that reaches a screen reader on a text input and not on
 * the select beside it is worse than no marker at all.
 *
 * And there is a **real space** in front of it — in the markup, deliberately
 * not as a margin in the stylesheet. Margins do not reach the accessibility
 * tree: without the text node the composed name is `"git remoterequired"`, a
 * screen reader says it as one word, and every `getByLabelText` in the product
 * silently stops matching. This is a separator, not spacing.
 */
function RequiredMark() {
  return (
    <>
      {" "}
      <span className={styles.labelRequired} data-test="field-required">
        required
      </span>
    </>
  )
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
export function FieldLabel({
  id,
  required = false,
  children,
}: {
  id?: string
  required?: boolean
  children: ReactNode
}) {
  return (
    <span className={styles.label} id={id}>
      {children}
      {required ? <RequiredMark /> : null}
    </span>
  )
}

/**
 * The field-hint voice — the rule the operator cannot see by looking at the
 * control — exported beside `FieldLabel` for the same reason.
 */
export function FieldHint({
  id,
  children,
}: {
  id?: string
  children: ReactNode
}) {
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
 *
 * `required` rides in the label rather than beside the control, and changes no
 * validation behaviour — see `RequiredMark` for why it is a word, why it is
 * readable by assistive tech, and why the space in front of it is load-bearing.
 */
export function Field({
  id,
  label,
  labelHidden = false,
  required = false,
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
        {required ? <RequiredMark /> : null}
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
