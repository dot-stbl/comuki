import { SearchField } from "@/shared/ui"

export interface KnowledgeSearchProps {
  value: string
  onValueChange: (next: string) => void
  /**
   * The measure. The kit's field is `inline-size: 100%` by design — it takes
   * the room the band gives it — so how wide this box is allowed to get is the
   * header's business, not the field's, and the page hands it in.
   */
  className?: string
}

/**
 * The one control that narrows this screen.
 *
 * It is the kit's `SearchField` with this screen's two words in it — the name
 * it announces itself by and the words on the empty box. Nothing else.
 *
 * It used to be a hand-built `<Search icon> + <input type="search">` drawn to
 * `DataTableToolbar`'s measurements, and its own comment called the absence of
 * a kit search field "a gap in the kit". That gap was filled; the comment
 * outlived it. This is the same control the toolbar uses, at the toolbar's own
 * density, so the two read as one control on two screens rather than as two
 * opinions about what a search box is.
 *
 * No `data-active`, for the reason the toolbar's own call site gives: the
 * words the operator typed are still in the box, which is the loudest reading
 * a filter has, and a rule that tinted itself as well would be saying it twice.
 */
export function KnowledgeSearch({
  value,
  onValueChange,
  className,
}: KnowledgeSearchProps) {
  return (
    <SearchField
      size="sm"
      value={value}
      onValueChange={onValueChange}
      data-test="knowledge-search"
      aria-label="Filter rules, docs and skills"
      placeholder="Search rules, docs, skills…"
      className={className}
    />
  )
}
