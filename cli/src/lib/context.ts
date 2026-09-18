/**
 * Compact context-window meter for the status line.
 *
 * `ctx 12k` plus an 8-char bar `ctx ▮▮▮▮▯▯▯▯`. Hidden when no token
 * data is present (the caller decides). Pure — `contextBar(used, window)`.
 */
export const DEFAULT_CONTEXT_WINDOW = 128_000

const BAR_WIDTH = 8
const FILLED = "▮"
const EMPTY = "▯"

/** Compact token count: `999`, `12k`, `128k`. */
export function tokensCompact(total: number): string {
  if (total < 1000) {
    return String(Math.max(0, Math.round(total)))
  }
  const thousands = total / 1000
  const rounded =
    thousands >= 10 ? Math.round(thousands).toString() : thousands.toFixed(1)
  return `${rounded.replace(/\.0$/, "")}k`
}

/**
 * 8-cell usage bar. `used`/`window` clamp to [0, 1]; a zero or
 * negative window is treated as empty (no divide-by-zero).
 */
export function contextBar(used: number, window: number): string {
  const ratio =
    window <= 0 ? 0 : Math.min(1, Math.max(0, used / window))
  const filled = Math.round(ratio * BAR_WIDTH)
  return `${FILLED.repeat(filled)}${EMPTY.repeat(BAR_WIDTH - filled)}`
}

/** Status-line chip: `ctx 12k ▮▮▮▮▯▯▯▯`. */
export function contextMeterLabel(used: number, window: number): string {
  return `ctx ${tokensCompact(used)} ${contextBar(used, window)}`
}
