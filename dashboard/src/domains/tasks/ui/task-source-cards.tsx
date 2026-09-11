import { Check, SquareKanban } from "lucide-react"

import { cn } from "@/shared/lib/utils"
import { PROVIDERS } from "@/domains/sources/model/providers"
import type { ProviderKey } from "@/domains/sources/model/types"
import { BrandIcon } from "@/shared/ui"

import styles from "./task-source-cards.module.css"

export interface TaskSourceCardsProps {
  value: ProviderKey
  onValueChange: (next: ProviderKey) => void
  disabled?: boolean
  "data-test"?: string
}

/**
 * The first question intake asks: where does this task come from.
 *
 * One card per row of the provider registry, in its order: the trackers a
 * connection can speak, and `native` for the product's own intake. Each card
 * is the entry read out — its mark, its word, and the line that says what
 * picking it means. Adding a provider adds a card and nothing else.
 *
 * A domain component rather than a kit one: `ChoiceField` is the kit's
 * version of a small closed set, and this borrows its construction wholesale
 * — a real `<input type="radio">` off-screen under each box, so the arrow-key
 * group, the single tab stop and the announced role are the platform's rather
 * than reimplemented — but the content is this product's provider registry,
 * which a kit part has no business knowing.
 *
 * Yandex Tracker is the one card without a brand mark (no monochrome mark is
 * published; draining the colour glyph leaves an unnameable shape — see
 * `Provider.brand`). It takes a lucide board glyph so the row keeps its
 * rhythm, and its name says the rest.
 */
export function TaskSourceCards({
  value,
  onValueChange,
  disabled = false,
  "data-test": dataTest,
}: TaskSourceCardsProps) {
  return (
    /* The picker carries no heading of its own: its home is a `FormCard`
       labelled "source" on the create page, and a second label above a
       card's own label would be the same word twice. The group still needs
       a name for assistive tech, so the fieldset keeps one — spoken, not
       drawn. */
    <fieldset
      className={styles.fieldset}
      aria-label="source"
      data-test={dataTest}
    >
      <div className={styles.cards}>
        {PROVIDERS.map((provider) => {
          const source = provider.key
          const selected = value === source
          /* A mark that already says the provider's name — the drained
             github/gitlab/jira glyphs, whose brand id *is* this provider —
             does not need the name spelled beside it; a mark that stands in
             for something else (the board glyph for yandex tracker, the
             product's own container for native) keeps its visible name. The
             name never leaves the radio's `aria-label`, so the group reads
             the same either way. */
          const selfNaming = provider.brand === source
          return (
            <label
              key={source}
              className={cn(styles.card, selected && styles.cardSelected)}
              data-test="task-source-card"
              data-value={source}
              data-selected={selected || undefined}
            >
              <input
                type="radio"
                name="task-source"
                className={styles.input}
                value={source}
                checked={selected}
                disabled={disabled}
                aria-label={provider.label}
                onChange={() => onValueChange(source)}
              />
              <span className={styles.mark}>
                {provider.brand ? (
                  <BrandIcon brand={provider.brand} size="lg" label={null} />
                ) : (
                  /* Sized by the class to sit exactly where the kit's own
                     scale puts a brand mark, so the spelled card and the
                     drawn ones keep one rhythm. */
                  <SquareKanban className={styles.brand} aria-hidden="true" />
                )}
              </span>
              {selfNaming ? null : (
                <span className={styles.name}>{provider.label}</span>
              )}
              <span className={styles.note}>{provider.intakeNote}</span>
              {/* Reserved on every card, filled on one — the ChoiceField
                  device, so a mark that appeared would move the name beside
                  it every time the selection moved. */}
              <Check className={styles.check} aria-hidden="true" />
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}
