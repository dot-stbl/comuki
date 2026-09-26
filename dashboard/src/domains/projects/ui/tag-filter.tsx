import { useTranslation } from "react-i18next"

import styles from "./tag-filter.module.css"

export interface TagFilterProps {
  /** The vocabulary the loaded rows carry — distinct and sorted. */
  availableTags: readonly string[]
  /** The tags currently narrowing the list. */
  selectedTags: readonly string[]
  /** Reports one tag flipped. Narrowing semantics stay with the owner. */
  onToggleTag: (tag: string) => void
}

/**
 * The registry's tag filter — toggle chips for the toolbar's leading slot.
 *
 * Presentational by contract: it renders the vocabulary it is handed and
 * reports each flip, while the AND-narrowing (a row shows when it carries
 * *every* selected tag) stays in the page memo that owns the rows. There is
 * no disabled state because the page never had one — the toolbar this lives
 * in is rendered only once the registry has loaded.
 *
 * With no vocabulary it renders nothing: the leading slot is absent rather
 * than occupied by an empty group, which is the state a registry whose rows
 * carry no tags has always been in.
 */
export function TagFilter({
  availableTags,
  selectedTags,
  onToggleTag,
}: TagFilterProps) {
  const { t } = useTranslation("projects")

  if (availableTags.length === 0) {
    return null
  }

  return (
    <div
      className={styles.tagFilter}
      data-test="project-tag-filter"
      role="group"
      aria-label={t("registry.filterByTag")}
    >
      {availableTags.map((tag) => {
        const selected = selectedTags.includes(tag)
        return (
          <button
            key={tag}
            type="button"
            className={styles.tagToggle}
            aria-pressed={selected}
            data-active={selected ? "" : undefined}
            data-test={`project-tag-filter-${tag}`}
            onClick={() => {
              onToggleTag(tag)
            }}
          >
            {tag}
          </button>
        )
      })}
    </div>
  )
}
