import type { KnowledgeEntry } from "@/domains/knowledge/model/types"
import { cn } from "@/shared/lib/utils"

import { KindMark, PinnedMark, RuleKindMark } from "./knowledge-badges"
import styles from "./knowledge-entry-row.module.css"

export interface KnowledgeEntryRowProps {
  entry: KnowledgeEntry
  selected: boolean
  onSelect: (id: string) => void
}

/**
 * One entry in the rule set, as a row that opens itself.
 *
 * A list rather than a table, because the reading is not column-wise: nobody
 * scans a rule set comparing revisions down a page. They look for the one rule
 * whose *summary* is the thing they are arguing about, and the summary is a
 * sentence — a column would truncate it into uselessness on every row.
 *
 * The whole row is the control, so there is no separate open affordance to aim
 * at. Selection is marked by a border and a wash the way the rail marks its
 * active item: never by a shadow, and never by a halo — the one zero-offset
 * ring in this product is the focus ring.
 */
export function KnowledgeEntryRow({
  entry,
  selected,
  onSelect,
}: KnowledgeEntryRowProps) {
  return (
    <button
      type="button"
      data-test="knowledge-entry"
      data-entry={entry.id}
      data-selected={selected || undefined}
      aria-pressed={selected}
      className={cn(styles.row, selected && styles.selected)}
      onClick={() => onSelect(entry.id)}
    >
      <span className={styles.head}>
        <span className={styles.title}>{entry.title}</span>
        <KindMark kind={entry.kind} />
        {entry.ruleKind ? <RuleKindMark ruleKind={entry.ruleKind} /> : null}
        {entry.pinned ? <PinnedMark /> : null}
        <span className={styles.revision}>@{entry.revision}</span>
      </span>

      {/* The entry's own words, and the reason the list is a list. Two lines at
          most: past that it is the detail sheet's job, and a row that grows
          with its prose stops being scannable. */}
      <span className={styles.summary}>{entry.summary}</span>

      <span className={styles.meta}>
        <span className={styles.scope}>{entry.scope}</span>
        <span className={styles.sep} aria-hidden="true">
          ·
        </span>
        <span>updated {entry.updated}</span>
      </span>
    </button>
  )
}
