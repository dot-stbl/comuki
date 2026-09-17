/* How long something took — two readings, and the reason they stay two.
 *
 * `formatDuration` lived in `domains/runs/model/format.ts`, which nine files
 * across four domains (`queue`, `compute`, `home`, `runs`) reached into for it.
 * A utility parked inside one domain's model is a utility the next domain does
 * not find, which is how `chat/ui/tool-call.tsx` came to carry its own.
 *
 * They are not the same function wearing two units. They are two shapes:
 *
 * - `formatDuration` is a **clock** — `mm:ss`, zero-padded, monotone width. It
 *   reads down a column of running work, and a column of clocks is scannable
 *   only because every row is the same shape.
 * - `formatDurationMs` is a **latency figure** — `420ms`, `31.4s`. It sits
 *   inline beside one tool call, where the difference between 40 and 400 is the
 *   whole point and `00:00` would say nothing at all.
 *
 * So one function with a unit flag would need a shape flag beside it, and a
 * call site that got the pair wrong would render something plausible. The unit
 * is in the name instead: the only way to read milliseconds as a clock is to
 * type the wrong function name, which a reviewer can see.
 */

/**
 * Elapsed **seconds** as a run clock: `00:00`, `01:15`, `25:30`.
 *
 * Seconds — not milliseconds. Every caller holds a `*Sec` field off the wire.
 * Negative and fractional inputs floor to zero and to whole seconds rather than
 * rendering `-1:-4`.
 */
export function formatDuration(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds))
  const minutes = Math.floor(safe / 60)
  const rem = safe % 60
  return `${String(minutes).padStart(2, "0")}:${String(rem).padStart(2, "0")}`
}

/**
 * A call's latency in **milliseconds**, in the unit the number deserves.
 *
 * Milliseconds up to a second, because that is the range where the difference
 * between 40 and 400 matters; seconds above it, because nobody reads
 * `31420ms`. One decimal and no more — a call that took half a minute is not
 * measured to the millisecond by anything the operator can act on.
 */
export function formatDurationMs(ms: number): string {
  return ms < 1000 ? `${Math.round(ms)}ms` : `${(ms / 1000).toFixed(1)}s`
}
