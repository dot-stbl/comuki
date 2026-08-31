import { CopyButton } from "./copy-button"
import styles from "./form.module.css"

export interface SecretValueProps {
  id: string
  label: string
  /** The secret itself. Held by the caller, and dropped by it. */
  value: string
  hint?: string
}

/**
 * A value the product produced, shown to be copied and then let go.
 *
 * Not an input: there is nothing to edit, and a read-only text box invites the
 * operator to try. It is a selectable block with a copy control beside it, and
 * it holds no state of its own — the secret lives in the caller for exactly as
 * long as the caller means it to, which is the only way "shown once" can be
 * true of anything.
 *
 * It writes its own label rather than borrowing `Field`'s, because `Field`
 * associates a `<label for>` with a form control and a `<code>` is not one — a
 * label pointing at it would simply be ignored. The block is focusable and
 * named through `aria-labelledby` instead, so the secret can be reached, read
 * and selected from the keyboard as well as the pointer.
 */
export function SecretValue({ id, label, value, hint }: SecretValueProps) {
  const labelId = `${id}-label`
  const hintId = `${id}-hint`

  return (
    <div className={styles.field}>
      <span className={styles.label} id={labelId}>
        {label}
      </span>
      <code
        className={styles.secret}
        id={id}
        tabIndex={0}
        aria-labelledby={labelId}
        aria-describedby={hint ? hintId : undefined}
        data-test="secret-value"
      >
        {value}
      </code>
      {hint ? (
        <span className={styles.hint} id={hintId}>
          {hint}
        </span>
      ) : null}
      <span>
        <CopyButton value={value} data-test="secret-copy" />
      </span>
    </div>
  )
}
