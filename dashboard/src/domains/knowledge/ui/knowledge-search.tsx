import { Search } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import styles from "./knowledge-search.module.css"

export interface KnowledgeSearchProps {
  value: string
  onValueChange: (next: string) => void
  className?: string
}

/**
 * The one control that narrows this screen.
 *
 * The kit's search lives inside `DataTableToolbar`, promoted out of a column's
 * own `meta.filter` — and there is no table under this box to declare one. So
 * the field is built here rather than a table being invented to own it, and it
 * is drawn to the toolbar's own measurements (`--h-button-sm`, the control
 * step, the surface colour, the data voice) so the two read as the same control
 * on two screens rather than as two opinions about what a search box is.
 *
 * **This is a gap in the kit**, not a preference: a screen that filters a list
 * of things that are not table rows has nowhere to get a search field from.
 *
 * A real `type="search"`, so the browser gives it its clearing affordance and
 * assistive tech announces it as a search rather than as one more text box.
 */
export function KnowledgeSearch({
  value,
  onValueChange,
  className,
}: KnowledgeSearchProps) {
  return (
    <div className={cn(styles.search, className)}>
      <Search className={styles.icon} aria-hidden="true" />
      <input
        type="search"
        className={styles.input}
        data-test="knowledge-search"
        aria-label="Filter rules, docs and skills"
        placeholder="Search rules, docs, skills…"
        value={value}
        onChange={(event) => {
          onValueChange(event.target.value)
        }}
      />
    </div>
  )
}
