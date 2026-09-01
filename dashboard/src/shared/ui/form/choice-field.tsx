import { Check } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import styles from "./form.module.css"

export interface ChoiceOption {
  value: string
  label: string
  /** The sentence that tells this option apart from the one above it. */
  description: string
}

export interface ChoiceFieldProps {
  /** Names the group for assistive tech. */
  name: string
  label: string
  value: string
  onValueChange: (next: string) => void
  options: readonly ChoiceOption[]
  disabled?: boolean
  "data-test"?: string
}

/**
 * A small set of options, each of which needs a sentence.
 *
 * Boxes rather than radio dots, for two reasons that point the same way: the
 * small round mark is a retired device in this world, and a row of three bare
 * words would be asking the operator to guess what the difference between them
 * is. Each option is a bordered box carrying its own name and its own line, so
 * the three are distinguishable by reading rather than by prior knowledge.
 *
 * Real `<input type="radio">` underneath, moved off-screen rather than removed:
 * that keeps the arrow-key group, the single tab stop and the announced role
 * that a div-with-`aria-checked` would have to reimplement. The box carries the
 * focus ring on the input's behalf.
 *
 * The heading is a `span`, not a `<legend>`: a legend renders in the fieldset's
 * border slot, outside the layout, so the gap between it and the first box is
 * the browser's legend rule rather than the `s2` every other field puts
 * between its label and its control. The fieldset keeps its group name through
 * `aria-label`.
 */
export function ChoiceField({
  name,
  label,
  value,
  onValueChange,
  options,
  disabled = false,
  "data-test": dataTest,
}: ChoiceFieldProps) {
  return (
    <fieldset
      className={styles.choices}
      aria-label={label}
      data-test={dataTest}
    >
      <span className={styles.label}>{label}</span>
      {options.map((option) => {
        const selected = value === option.value
        return (
          <label
            key={option.value}
            className={cn(styles.choice, selected && styles.choiceSelected)}
            data-test={`${name}-option`}
            data-value={option.value}
            data-selected={selected || undefined}
          >
            <input
              type="radio"
              name={name}
              className={styles.choiceInput}
              value={option.value}
              checked={selected}
              disabled={disabled}
              onChange={() => onValueChange(option.value)}
            />
            <span className={styles.choiceHead}>
              <Check className={styles.choiceMark} aria-hidden="true" />
              {option.label}
            </span>
            <span className={styles.choiceNote}>{option.description}</span>
          </label>
        )
      })}
    </fieldset>
  )
}
