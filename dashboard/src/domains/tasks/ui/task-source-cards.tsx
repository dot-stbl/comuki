import { Check, SquareKanban } from "lucide-react"

import { cn } from "@/shared/lib/utils"
import {
  TASK_SOURCES,
  TASK_SOURCE_BRAND,
  TASK_SOURCE_LABEL,
  TASK_SOURCE_NOTE,
} from "@/domains/tasks/model/task-sources"
import type { TaskSource } from "@/domains/tasks/model/types"
import { BrandIcon } from "@/shared/ui"

import styles from "./task-source-cards.module.css"

export interface TaskSourceCardsProps {
  value: TaskSource
  onValueChange: (next: TaskSource) => void
  disabled?: boolean
  "data-test"?: string
}

/**
 * The first question intake asks: where does this task come from.
 *
 * One card per provider in the platform's own vocabulary — the four trackers
 * a connection can speak, and `manual` for the product's own intake — each
 * carrying its drained brand mark, its name, and the line that says what
 * picking it means (`TASK_SOURCE_NOTE`, in the model, because the backlog's
 * badge reads the same vocabulary and the two must not drift).
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
 * `task-sources.ts`). It takes a lucide board glyph so the row keeps its
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
        {TASK_SOURCES.map((source) => {
          const selected = value === source
          const brand = TASK_SOURCE_BRAND[source]
          /* A mark that already says the provider's name — the drained
             github/gitlab/jira glyphs — does not need the name spelled
             beside it; a mark that does not (the board glyph standing in
             for yandex tracker, the product's own mark for manual) keeps
             its visible name. The name never leaves the radio's
             `aria-label`, so the group reads the same either way. */
          const selfNaming =
            brand !== null && TASK_SOURCE_LABEL[source] === source
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
                aria-label={TASK_SOURCE_LABEL[source]}
                onChange={() => onValueChange(source)}
              />
              <span className={styles.mark}>
                {brand ? (
                  <BrandIcon brand={brand} size="lg" label={null} />
                ) : (
                  /* Sized by the class to sit exactly where the kit's own
                     scale puts a brand mark, so the spelled card and the
                     drawn ones keep one rhythm. */
                  <SquareKanban className={styles.brand} aria-hidden="true" />
                )}
              </span>
              {selfNaming ? null : (
                <span className={styles.name}>{TASK_SOURCE_LABEL[source]}</span>
              )}
              <span className={styles.note}>{TASK_SOURCE_NOTE[source]}</span>
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
