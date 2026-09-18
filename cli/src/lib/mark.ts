/**
 * Printable-ASCII reductions of dashboard/public/favicon.svg. The mark
 * is a perspective freight container: a broad loading-side slab on the
 * left and two front doors on the right, split around their latch gap.
 * Density, not pseudo-antialiasing, carries the silhouette.
 */
import { paint } from "../theme"

/** One-row silhouette for stable chrome. */
export const MARK_COMPACT = "[/###/|## ##|]"

/** Compatibility name for existing chrome consumers; the value is the compact grid. */
export const MARK_SMALL: readonly string[] = [MARK_COMPACT, "", MARK_COMPACT]

const welcomeSource = [
  "             /########\\",
  "       /#####/##########\\",
  "   /########/####  ##  ####\\",
  " /#########/#####  ##  #####|",
  "|##########|#####--##--#####|",
  " \\#########\\#####  ##  #####|",
  "   \\########\\####  ##  ####/",
  "       \\#####\\##########/",
  "             \\########/",
] as const

const welcomeWidth = Math.max(...welcomeSource.map((line) => line.length))

/** Large welcome-only silhouette, normalized to one glyph grid. */
export const MARK_WELCOME: readonly string[] = welcomeSource.map((line) =>
  line.padEnd(welcomeWidth)
)

/** Paints every line of a mark with `color`, resetting after each. */
export function paintMark(
  lines: readonly string[],
  color: string
): readonly string[] {
  return lines.map((line) => paint(line, color))
}
