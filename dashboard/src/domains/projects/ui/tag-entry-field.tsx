import { useState } from "react"
import type { KeyboardEvent, ReactNode } from "react"
import { X } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Field } from "@/shared/ui"

import styles from "./tag-entry-field.module.css"

export interface TagEntryFieldProps {
  /** The control's id. The label points at it, so it is required. */
  id: string
  /** The committed chips, normalised — the same array the wire will carry. */
  value: string[]
  onValueChange: (next: string[]) => void
  disabled?: boolean
  /** The rule the operator cannot see by looking at the box. */
  hint?: ReactNode
}

/**
 * Tag entry as chips: type, press Enter or comma, it commits; Backspace on an
 * empty field removes the last chip; the × on a chip removes that one.
 *
 * One component for the two surfaces that collect a project's vocabulary —
 * the create form and the detail page's identity editor — because a tag entry
 * that worked differently in the two places would be two vocabularies, and
 * because the interactions (the intercepted comma, the duplicate refused, the
 * draft that never becomes part of a chip) are rules about the *field*, not
 * about either screen.
 *
 * Commit normalises the way the server will store the tag — trimmed,
 * lower-cased — so what the operator sees in a chip is what every list will
 * show. A duplicate is refused because the second copy says nothing the first
 * did not; the refusal is silent, which is the correct loudness for something
 * that changes nothing.
 */
export function TagEntryField({
  id,
  value,
  onValueChange,
  disabled = false,
  hint,
}: TagEntryFieldProps) {
  const { t } = useTranslation("projects")
  const [draft, setDraft] = useState("")

  const commit = (raw: string) => {
    const tag = raw.trim().toLowerCase()
    if (tag === "" || value.includes(tag)) {
      return
    }
    onValueChange([...value, tag])
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter" || event.key === ",") {
      // The comma is the separator, not a character in a tag — intercept it
      // or every chip would end with a comma nobody typed as a word.
      event.preventDefault()
      commit(draft)
      setDraft("")
      return
    }
    if (event.key === "Backspace" && draft === "" && value.length > 0) {
      onValueChange(value.slice(0, -1))
    }
  }

  return (
    <Field id={id} label={t("identity.tags")} hint={hint}>
      <div className={styles.tagField}>
        {value.length > 0 ? (
          <ul className={styles.chips} data-test="tag-entry-chips">
            {value.map((tag) => (
              <li key={tag} className={styles.chip}>
                <span className={styles.chipText}>{tag}</span>
                <button
                  type="button"
                  className={styles.remove}
                  aria-label={t("tagEntry.remove", { tag })}
                  data-test={`tag-entry-remove-${tag}`}
                  disabled={disabled}
                  onClick={() => {
                    onValueChange(value.filter((entry) => entry !== tag))
                  }}
                >
                  <X aria-hidden="true" />
                </button>
              </li>
            ))}
          </ul>
        ) : null}
        <input
          id={id}
          type="text"
          className={styles.input}
          value={draft}
          disabled={disabled}
          spellCheck={false}
          autoComplete="off"
          placeholder={t("tagEntry.placeholder")}
          data-test="tag-entry-input"
          onChange={(event) => {
            // A comma in the middle of a paste commits everything before it;
            // a trailing comma never becomes part of a chip.
            const next = event.target.value
            if (next.includes(",")) {
              for (const part of next.split(",")) {
                commit(part)
              }
              setDraft("")
              return
            }
            setDraft(next)
          }}
          onKeyDown={onKeyDown}
        />
      </div>
    </Field>
  )
}
