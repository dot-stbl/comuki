import type { CSSProperties } from "react"

/**
 * The accent as one inline custom property — the sanctioned shape for
 * data-driven colour (design D7). The value stays addressable by the
 * stylesheet (the dot and the tag chips read it), and an absent colour simply
 * leaves the property unset, which the consumers handle with a token
 * fallback. Never an inline `background-color`.
 *
 * Its own module rather than a second export beside `ProjectMark` because a
 * component file that also exports a function breaks the fast-refresh
 * boundary the linter holds.
 */

/**
 * A style carrying exactly the one custom property — `CSSProperties` does not
 * know custom properties, so the intersection spells the key instead of
 * asserting the object into shape.
 */
export type ProjectAccentStyle = CSSProperties &
  Record<"--project-accent", string>

export function projectAccentStyle(
  color: string | null
): ProjectAccentStyle | undefined {
  return color ? { "--project-accent": color } : undefined
}
