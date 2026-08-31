import type { Ref } from "react"

import { cn } from "@/shared/lib/utils"

import { ComukiMark } from "../comuki-mark"
import { BRAND_MARKS, type BrandId } from "./brand-marks"
import styles from "./brand-icon.module.css"

/** The icon scale, by its own names. Same four steps every kit control uses. */
export type BrandIconSize = "xs" | "sm" | "md" | "lg"

export interface BrandIconProps {
  brand: BrandId
  /**
   * A step from `--icon-xs/sm/md/lg`, never a length.
   *
   * The same contract `Button` enforces on its descendant SVGs: an icon takes
   * its size from the scale, so a mark sits at the cap height of the type
   * beside it and a call site cannot invent a fifth size. `xs` is the table-row
   * step and the smallest a detailed mark survives — below it the Docker whale
   * and the GitLab tanuki are shapes rather than marks.
   */
  size?: BrandIconSize
  /**
   * The accessible name.
   *
   * Omit it and the mark answers to the vendor's own spelling. Pass a string to
   * say it in the surface's own words — the sources table calls GitHub
   * `github`, because that is the word its filter, its form and its column all
   * use, and a row that named it two ways would be two vocabularies.
   *
   * Pass `null` **only** when something else in the same control already names
   * the provider — a button with its own label, a badge with the word beside
   * the mark. A mark alone is not a label, and a decorative mark alone is a
   * blank cell to anybody not looking at it.
   */
  label?: string | null
  className?: string
  ref?: Ref<SVGSVGElement>
}

/**
 * A provider's own mark, drained to the chrome it sits in.
 *
 * Colour in this product belongs to the data, so a mark arrives in
 * `currentColor` rather than in its brand palette — eight vendor hues in the
 * table chrome would break the one rule the whole system is built on. The cost
 * is real and is stated where it is paid: a mark that is only recognisable by
 * its colour does not survive this treatment, and those are not drawn at all
 * rather than drawn badly (see `brand-marks.ts` for which, and why).
 *
 * Because the mark is monochrome and small, **the name is the component's job,
 * not the call site's**. `label` defaults to the vendor's name and only
 * `label={null}` turns it off, so the failure mode of forgetting is a mark that
 * is named twice rather than one that is named not at all.
 */
export function BrandIcon({
  brand,
  size = "md",
  label,
  className,
  ref,
}: BrandIconProps) {
  const mark = BRAND_MARKS[brand]
  const named = label !== null
  const name = label ?? mark.title

  const classes = cn(
    styles.icon,
    styles[size],
    mark.wide && styles.wide,
    className
  )

  // The one mark the kit already ships as a component of its own. It is drawn
  // through that component rather than through a second copy of its path, so
  // the product's own object can never be one revision behind itself.
  if (!mark.art) {
    return (
      <ComukiMark
        ref={ref}
        className={classes}
        data-test="brand-icon"
        data-brand={brand}
        role={named ? "img" : undefined}
        aria-label={named ? name : undefined}
        aria-hidden={named ? undefined : true}
      />
    )
  }

  return (
    <svg
      ref={ref}
      viewBox={mark.art.viewBox}
      fill="currentColor"
      focusable="false"
      className={classes}
      data-test="brand-icon"
      data-brand={brand}
      role={named ? "img" : undefined}
      aria-label={named ? name : undefined}
      aria-hidden={named ? undefined : true}
    >
      <path d={mark.art.path} />
    </svg>
  )
}
