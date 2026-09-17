/* How long ago, in the one vocabulary the product speaks.
 *
 * Four domains had each written this, and each had written it slightly
 * differently: `chat/api/mappers.ts` said `3m ago`, `tasks/api/mappers.ts` said
 * `8 min` (its docstring admitted the copy outright), `approvals/api/mappers.ts`
 * said `12 min` but had no bucket above hours at all, and
 * `models/model/keys.ts` said `7 days ago` in a ladder made only of days. The
 * same fact, on four screens of one console, in four spellings.
 *
 * The spelling kept here is the one the seeds already speak — `just now`,
 * `8 min`, `2 h`, `3 d`. Three of the four domains and every mock row were
 * already saying it, so it is the product's word rather than a new one, and it
 * is the terse register the rest of the console is written in: a figure and its
 * unit, no sentence around it.
 *
 * **No `ago`.** The word belongs to the caller's sentence, not to the reading:
 * these strings sit in a column headed `age`, in a `<dl>` row labelled
 * `last used`, in a chip beside a session title — every one of which has
 * already said "ago" in its own way. Only the future needs a word of its own,
 * because nothing else in the string distinguishes `in 12 d` from `12 d`.
 *
 * Floors rather than rounds, in both directions: `8 min` means at least eight
 * minutes have passed, which is the claim an age is making. Rounding would let
 * a reading run ahead of the fact it reports.
 */

const MINUTE = 60_000
const HOUR = 60 * MINUTE
const DAY = 24 * HOUR

/**
 * A signed distance in milliseconds, as the console's words for it.
 *
 * Positive is the past (`now - then`), negative is ahead (`in 12 d`). The unit
 * is in the parameter name on purpose — every caller here holds either an
 * instant difference or a `*Sec` field, and a seconds value passed in silently
 * reads a fortnight as a minute.
 */
export function formatRelativeTime(deltaMs: number): string {
  const size = Math.abs(deltaMs)
  if (size < MINUTE) {
    // Below a minute there is nothing worth counting in either direction, and
    // "in 0 min" is not a reading anybody wants.
    return "just now"
  }
  const reading =
    size < HOUR
      ? `${Math.floor(size / MINUTE)} min`
      : size < DAY
        ? `${Math.floor(size / HOUR)} h`
        : `${Math.floor(size / DAY)} d`
  return deltaMs < 0 ? `in ${reading}` : reading
}

/**
 * An ISO instant as the same reading, or `null` when it cannot be read.
 *
 * `null` rather than a fallback string: the three mappers that call this each
 * spell absence their own way — the chat rail renders nothing, the decision
 * card renders an em dash — and a helper that picked one of those for them
 * would be making a rendering decision from inside the wire layer.
 */
export function formatRelativeInstant(
  iso: string,
  nowMs: number = Date.now()
): string | null {
  const at = Date.parse(iso)
  return Number.isNaN(at) ? null : formatRelativeTime(nowMs - at)
}
