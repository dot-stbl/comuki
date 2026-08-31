import { cn } from "@/shared/lib/utils"

import { BrandIcon, type BrandIconSize } from "./brand-icon"
import type { BrandId } from "./brand-marks"
import styles from "./brand-tag.module.css"

export interface BrandTagProps {
  /**
   * The mark to draw, or `null` for a provider this kit has no honest mark
   * for — in which case the tag says the provider's name instead.
   *
   * `null` is a first-class answer rather than a missing case. There are
   * providers whose mark cannot survive being drained to `currentColor`, and
   * the alternative to spelling those out is drawing a trademark from memory.
   */
  brand: BrandId | null
  /**
   * The provider's name, in this surface's own voice — and not optional in
   * either branch. It is the mark's accessible name when there is a mark, the
   * cell's text when there is not, and the hover reading in both cases.
   */
  label: string
  size?: BrandIconSize
  className?: string
}

/**
 * A provider, said as its mark where one exists and as its name where one does
 * not.
 *
 * This is the component that carries the rule, so no screen has to remember it:
 * a mark alone is not a label, so the name always travels with it — as the
 * accessible name for anyone not looking, and as a hover reading for anyone
 * looking at a monochrome glyph they do not happen to recognise.
 *
 * **The hover reading is a `title`, deliberately, and not the kit's `Tooltip`.**
 * `Tooltip` is built on React Aria's `Focusable`, which makes its trigger a tab
 * stop and warns when the trigger has no interactive role — correct for an
 * icon-only button, wrong for a table cell. Wrapping one of these would put a
 * dead tab stop on every row of a virtualized list to describe something the
 * reader cannot act on. The kit tooltip belongs on controls; a cell keeps the
 * plain attribute every other cell in these tables already uses.
 */
export function BrandTag({
  brand,
  label,
  size = "sm",
  className,
}: BrandTagProps) {
  if (!brand) {
    return (
      <span
        className={cn(styles.word, className)}
        data-test="brand-tag"
        data-brand="none"
      >
        {label}
      </span>
    )
  }

  return (
    <span
      className={cn(styles.tag, className)}
      data-test="brand-tag"
      data-brand={brand}
      title={label}
    >
      <BrandIcon brand={brand} size={size} label={label} />
    </span>
  )
}
