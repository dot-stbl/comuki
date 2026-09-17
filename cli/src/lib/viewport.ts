/**
 * Pure transcript-viewport math — the irssi/htop scrolling model.
 *
 * The viewport shows a `height`-line window into the flattened
 * transcript. `offset` counts the lines hidden *below* the window:
 * `0` = glued to the bottom (follow mode — newest line visible,
 * auto-scrolls on new output), growing offset scrolls back up. The
 * whole model is three pure functions over (lines, height, offset) so
 * every boundary is unit-testable without Ink.
 */
export const NEW_MESSAGES_INDICATOR = "↓ new messages"

/** Largest scroll-back offset: everything before the last `height` lines. */
export function maxOffset(lineCount: number, height: number): number {
  return Math.max(0, lineCount - Math.max(0, height))
}

/** Clamps an offset into `[0, maxOffset]`; negative and past-max both settle. */
export function clampOffset(
  lines: readonly string[],
  height: number,
  offset: number
): number {
  return Math.min(Math.max(0, offset), maxOffset(lines.length, height))
}

/**
 * The visible window: the `height` lines ending at `lines.length - offset`.
 * A transcript shorter than the viewport returns itself whole; a
 * non-positive height renders nothing.
 */
export function viewportSlice(
  lines: readonly string[],
  height: number,
  offset: number
): string[] {
  if (height <= 0) {
    return []
  }
  const end = lines.length - clampOffset(lines, height, offset)
  const start = Math.max(0, end - height)
  return lines.slice(start, end)
}
