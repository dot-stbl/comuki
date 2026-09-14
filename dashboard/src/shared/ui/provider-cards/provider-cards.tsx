import { Check } from "lucide-react"
import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import { FieldHint, FieldLabel } from "../form"

import styles from "./provider-cards.module.css"

export interface ProviderCardOption {
  /** The radio's value. */
  value: string
  /**
   * The card's accessible name. Spoken by the radio even when the mark says
   * the word — nobody listening rather than looking loses it.
   */
  label: string
  /** The mark at the top of the card. Omitted on a card that is all words. */
  mark?: ReactNode
  /**
   * The drawn name. Omitted when the mark already says it — a card whose
   * glyph *is* its provider does not spell the word twice beside it.
   */
  name?: string
  /** The line that tells this card apart from the one above it. */
  note?: string
}

export interface ProviderCardsProps {
  /** Names the group for assistive tech — spoken, and drawn unless hidden. */
  label: string
  /**
   * The radios' one `name` — what makes them an arrow-key group with a
   * single tab stop rather than five separate controls.
   */
  name: string
  value: string
  onValueChange: (next: string) => void
  options: readonly ProviderCardOption[]
  /**
   * The label is real but not drawn. For a home that already says the word
   * above the cards — a `FormCard` of its own — so a second label would be
   * the same word twice.
   */
  labelHidden?: boolean
  /** The rule the operator cannot see by looking at the cards. */
  hint?: ReactNode
  disabled?: boolean
  "data-test"?: string
  /** Each card's own `data-test`. The value rides beside it as `data-value`. */
  cardDataTest?: string
}

/**
 * A closed set asked as a row of cards: a mark, a name and a line each,
 * one of them chosen.
 *
 * The construction is `ChoiceField`'s, scaled up for content that earns the
 * room — the kit's own small-closed-set part — so the behaviour is the
 * platform's rather than reimplemented: a real `<input type="radio">` lies
 * off-screen under each card, which is what an arrow-key group, a single
 * tab stop and the announced role are made of. The selected state is the
 * kit's selected-box language — a brand border, a 10% wash, and a check
 * reserved on every card so nothing inside it moves when the selection
 * does — with the border carrying its *weight* as an inset ring rather
 * than a second border pixel.
 *
 * A composite primitive rather than a content-aware one: the kit knows the
 * *shape* of a provider card (a mark, a name, a note) and nothing about
 * which providers there are. The registry, the marks and the sentences are
 * the caller's — a kit part has no business knowing a product's provider
 * list, and `shared/ui` cannot reach a domain. Both call sites today
 * (`/sources/new` and the task intake) read the same registry and hand it
 * here as options.
 */
export function ProviderCards({
  label,
  name,
  value,
  onValueChange,
  options,
  labelHidden = false,
  hint,
  disabled = false,
  "data-test": dataTest,
  cardDataTest,
}: ProviderCardsProps) {
  return (
    <fieldset
      className={styles.fieldset}
      aria-label={label}
      data-test={dataTest}
    >
      {labelHidden ? null : <FieldLabel>{label}</FieldLabel>}
      <div className={styles.cards}>
        {options.map((option) => {
          const selected = value === option.value
          return (
            <label
              key={option.value}
              className={cn(styles.card, selected && styles.cardSelected)}
              data-test={cardDataTest}
              data-value={option.value}
              data-selected={selected || undefined}
            >
              <input
                type="radio"
                name={name}
                className={styles.input}
                value={option.value}
                checked={selected}
                disabled={disabled}
                aria-label={option.label}
                onChange={() => onValueChange(option.value)}
              />
              {option.mark ? (
                <span className={styles.mark}>{option.mark}</span>
              ) : null}
              {option.name ? (
                <span className={styles.name}>{option.name}</span>
              ) : null}
              {option.note ? (
                <span className={styles.note}>{option.note}</span>
              ) : null}
              {/* Reserved on every card, filled on one — the ChoiceField
                  device, so a mark that appeared would move the name beside
                  it every time the selection moved. */}
              <Check className={styles.check} aria-hidden="true" />
            </label>
          )
        })}
      </div>
      {hint ? <FieldHint>{hint}</FieldHint> : null}
    </fieldset>
  )
}
