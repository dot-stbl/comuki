/**
 * The typed CLI flag shape the `resolveModes` function consumes.
 *
 * `yargs` does not produce a single typed bag — its parsed object is
 * `Record<string, unknown>` until the consumer narrows it. `Args` is
 * the narrow shape `resolveModes` actually reads; `bin/comuki.ts`
 * constructs it from `argv` before calling `resolveModes`.
 *
 * Keep this file tiny: only fields the mode resolver reads. Other
 * global options (--url, --api-key, --json, --message) live elsewhere.
 */
export interface Args {
  /** Explicit render mode: 'linear' forces the screen-reader renderer. */
  readonly mode?: "linear" | string
  /** Machine envelope output (NDJSON on stdout). Wins over --mode linear. */
  readonly machine?: boolean
  /** Skip animations / fade-in. */
  readonly reducedMotion?: boolean
  /** Bold + accent for state, not just hue. */
  readonly highContrast?: boolean
  /** Strip ANSI colour from the OpenTUI renderer. */
  readonly noColor?: boolean
  /** Replace box-drawing / arrows with ASCII glyphs. */
  readonly ascii?: boolean
  /** Disable mouse capture; rely on keystrokes only. */
  readonly noMouse?: boolean
  /** Clamp ambiguous-width characters to 1 column. */
  readonly unicodeNarrow?: boolean
}