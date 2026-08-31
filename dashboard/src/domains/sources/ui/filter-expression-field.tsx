import { FILTER_FIELD_NAMES } from "@/domains/sources/ui/filter-field-names"
import type { SourceKind } from "@/domains/sources/model/types"
import { TextareaField } from "@/shared/ui"

import styles from "./filter-expression-field.module.css"

export interface FilterExpressionFieldProps {
  id: string
  kind: SourceKind
  value: string
  onValueChange: (next: string) => void
  disabled?: boolean
}

/**
 * The watch's filter expression — **deliberately unparsed**.
 *
 * The requirements say "DSL TBD", and that is the most important sentence in
 * §6. Nobody has decided whether this language is jql-like, a label set, a
 * boolean expression, or something each connector compiles for its own
 * provider. So this field is built so the decision is still open:
 *
 *   - **No parser.** The value is a string from the moment it is typed to the
 *     moment the store holds it. Nothing here or in `sources.store.ts`
 *     tokenises, trims a clause, normalises whitespace or rewrites it.
 *   - **No validation that would pretend to a grammar.** There is no syntax
 *     error state, because there is no syntax to be wrong about. An empty
 *     expression is a legal value meaning "everything this connection can see"
 *     — not an incomplete form.
 *   - **No autocomplete.** The chips below the box are the field *names* the
 *     providers are known to carry, and pressing one appends the bare word.
 *     They insert no colon, no equals, no quotes and no boolean, because
 *     inserting punctuation would be asserting a separator — and a separator
 *     asserted by a UI affordance is exactly how a language gets decided by
 *     accident.
 *
 * The day the language exists, a parser goes beside this component and the
 * stored strings are migrated. That is a smaller job than unpicking a grammar
 * this screen invented while nobody was looking.
 */
export function FilterExpressionField({
  id,
  kind,
  value,
  onValueChange,
  disabled = false,
}: FilterExpressionFieldProps) {
  const names = FILTER_FIELD_NAMES[kind]

  const append = (name: string) => {
    const trimmed = value.trimEnd()
    onValueChange(trimmed.length === 0 ? name : `${trimmed}\n${name}`)
  }

  return (
    <div className={styles.wrap}>
      <TextareaField
        id={id}
        label="filter expression"
        voice="code"
        value={value}
        onValueChange={onValueChange}
        disabled={disabled}
        spellCheck={false}
        rows={4}
        placeholder="leave empty to admit everything this connection can see"
        data-test="filter-expression"
      />

      {names.length > 0 ? (
        <div className={styles.hints}>
          <span className={styles.hintsLabel} id={`${id}-hints`}>
            fields seen on {kind} tickets
          </span>
          {names.map((name) => (
            <button
              key={name}
              type="button"
              className={styles.hint}
              disabled={disabled}
              data-test="filter-hint"
              aria-describedby={`${id}-hints`}
              onClick={() => append(name)}
            >
              {name}
            </button>
          ))}
        </div>
      ) : null}

      <p className={styles.verbatim} data-test="filter-verbatim">
        stored verbatim and not parsed — the filter language is not decided yet,
        so this text is handed to the connector exactly as it is typed.
      </p>
    </div>
  )
}
