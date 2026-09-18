/**
 * The Comuki glyph as hand-authored ASCII — the SVG mark is a left
 * slab plus two right pillars with a gapped crossbar, sitting in a
 * rounded rect. Windows consoles drop the Unicode freight (◆ ⏺ ▌ ⠋),
 * so the identity lives here as printable ASCII only.
 *
 * SMALL is the welcome lockup. TINY is the three-row thinking pulse:
 * the crossbar breathes in and out. `paintMark` is the one color pass.
 */
import { paint } from "../theme"

/**
 * Welcome lockup, 11 cols × 5 rows: left slab and two pillars joined
 * by a mid crossbar — the H-like comuki glyph, simplified.
 */
export const MARK_SMALL: readonly string[] = [
  "+---+  |  |",
  "|   |  |  |",
  "|   |  +--+",
  "|   |  |  |",
  "+---+  |  |",
]

/**
 * Thinking-row frames, 3 cols × 3 rows. The two hashes are the pillars;
 * the middle row fills as the crossbar appears. Cycle: empty → thin →
 * full → thin.
 */
export const MARK_TINY_FRAMES: readonly (readonly string[])[] = [
  ["# #", "   ", "# #"],
  ["# #", " - ", "# #"],
  ["# #", "===", "# #"],
  ["# #", " - ", "# #"],
]

/** Paints every line of a mark with `color`, resetting after each. */
export function paintMark(
  lines: readonly string[],
  color: string
): readonly string[] {
  return lines.map((line) => paint(line, color))
}
