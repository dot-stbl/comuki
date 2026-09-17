/**
 * The one accent palette of the terminal-native UI (minimal structure,
 * Comuki brand colour): no boxes, no borders — the terminal IS the
 * chrome, hierarchy comes from spacing, weight and the deck's
 * restrained status colours. Raw ANSI escapes instead of Ink
 * `<Text color>` so the pure formatters in `lib/format.ts` can build
 * finished strings without React.
 *
 * Source of truth: DESIGN.md — the **Dichromat deck, dark reading**
 * (the dashboard's committed default theme). Truecolor escapes
 * (`38;2;R;G;B`) because the deck's primitives have no 256-cube
 * equivalents worth snapping to. Dichromat discipline: colour never
 * carries status alone — every status pairs with its word (`ok`,
 * `error`), and success=lavender / failed=yellow is deliberate.
 */

/** Ink-facing hex tokens — `<Text color>` props cannot take ANSI codes. */
export const palette = {
  /** status-running — the ◆ brand mark, spinner, streaming cursor. */
  brand: "#8787f3",
  /** deck text — bold reading text. */
  text: "#e8e8ee",
  /** status-success — pale lavender. */
  ok: "#d7d7ff",
  /** status-failed — yellow. */
  error: "#d2d228",
  /** status-waiting. */
  waiting: "#b4b442",
} as const

export const colors = {
  /** deck `text` #e8e8ee — reading text on dark. */
  text: "\x1b[38;2;232;232;238m",
  /** deck `text-muted` #b8b8bd — the quiet tier: bullets, labels, meta. */
  dim: "\x1b[38;2;184;184;189m",
  /** deck `text-muted` again — `dim` and `muted` are one tier, two names. */
  muted: "\x1b[38;2;184;184;189m",
  /** deck `text-faint` #8a8a8f — notices and other background noise. */
  faint: "\x1b[38;2;138;138;143m",
  /** deck `status-running` #8787f3 — accent, spinner, streaming cursor. */
  accent: "\x1b[38;2;135;135;243m",
  /** deck `status-success` #d7d7ff — pale lavender, never green. */
  ok: "\x1b[38;2;215;215;255m",
  /** deck `status-failed` #d2d228 — yellow, never red. */
  error: "\x1b[38;2;210;210;40m",
  /** deck `status-waiting` #b4b442. */
  waiting: "\x1b[38;2;180;180;66m",
  /** deck `rule` #37373c — frame lines (plan card, code-block borders). */
  rule: "\x1b[38;2;55;55;60m",
  bright: "\x1b[1m", // bold — weight hierarchy, no hue
  italic: "\x1b[3m",
  strike: "\x1b[9m",
  underline: "\x1b[4m",
  reset: "\x1b[0m",
} as const

export const symbols = {
  prompt: "›",
  checkmark: "✓",
  cross: "✗",
  bullet: "·",
  arrow: "→",
  /** The one bullet every collapsed event line (thinking, tools) leads with. */
  event: "⏺",
  /** The assistant's brand glyph — the freight mark of the swarm lead. */
  brandMark: "◆",
  spinnerFrames: ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"],
} as const

/**
 * The single left gutter every transcript line shares — one space that
 * keeps the whole conversation off the terminal edge.
 */
export const gutter = " "

/** Wraps `text` in an ANSI color, resetting after. */
export function paint(text: string, color: string): string {
  return color + text + colors.reset
}

/** Strips ANSI escapes — used by width-aware truncation and tests. */
export function stripAnsi(text: string): string {
  // eslint-disable-next-line no-control-regex
  return text.replace(/\x1b\[[0-9;]*m/g, "")
}

/** Advances a spinner frame index, wrapping back to zero. */
export function nextSpinnerFrame(
  frame: number,
  frameCount: number = symbols.spinnerFrames.length
): number {
  return (frame + 1) % frameCount
}

// ---------------------------------------------------------------------------
// Message identity marks
// ---------------------------------------------------------------------------

/**
 * The identity chrome of one transcript row: which glyph leads it, what
 * color the glyph wears, and the dim speaker label beside it (assistant
 * only). Derived from the role — never hand-picked at a call site.
 * User rows carry no glyph at all: the bare bold text at column 0 is
 * the identity (minimal — no prefix, no label).
 */
export interface MessageMark {
  /** Leading glyph; empty when the row leads with bare text (user). */
  readonly glyph: string
  /** ANSI color of the glyph. */
  readonly glyphColor: string
  /** Speaker label rendered beside the glyph; empty when the glyph alone identifies. */
  readonly label: string
  /** ANSI color of the label. */
  readonly labelColor: string
  /** ANSI color of the row's text; empty string = terminal default. */
  readonly textColor: string
}

/** Role → identity chrome. Unknown roles read as quiet system rows. */
export function messageMark(role: string): MessageMark {
  if (role === "user") {
    return {
      glyph: "",
      glyphColor: "",
      label: "",
      labelColor: "",
      textColor: colors.bright + colors.text,
    }
  }
  if (role === "assistant") {
    return {
      glyph: symbols.brandMark,
      glyphColor: colors.accent,
      label: "comuki",
      labelColor: colors.dim,
      textColor: "",
    }
  }
  return {
    glyph: symbols.bullet,
    glyphColor: colors.muted,
    label: "",
    labelColor: "",
    textColor: colors.muted,
  }
}
