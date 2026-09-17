/**
 * The one accent palette of the terminal-native UI (option A, owner
 * approved): no boxes, no borders — the terminal IS the chrome. Raw ANSI
 * escapes instead of Ink `<Text color>` so the pure formatters in
 * `lib/format.ts` can build finished strings without React.
 *
 * One accent (slate-blue, the brand periwinkle of DESIGN.md's
 * status-running) for statuses and the prompt; thinking is dimmed gray,
 * tools muted mono, results in the terminal's default color.
 */

/** Ink-facing hex tokens — `<Text color>` props cannot take ANSI codes. */
export const palette = {
  brand: "#8787f3",
} as const

export const colors = {
  accent: "\x1b[38;5;104m", // slate-blue
  dim: "\x1b[2m", // dimmed gray
  bright: "\x1b[1m", // bold white
  italic: "\x1b[3m",
  strike: "\x1b[9m",
  underline: "\x1b[4m",
  green: "\x1b[32m",
  red: "\x1b[31m",
  yellow: "\x1b[33m",
  blue: "\x1b[34m",
  reset: "\x1b[0m",
  muted: "\x1b[38;5;245m", // muted gray for tools
} as const

export const symbols = {
  prompt: "›",
  checkmark: "✓",
  cross: "✗",
  bullet: "·",
  arrow: "→",
  thinking: "◌",
  tool: "⚙",
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
 */
export interface MessageMark {
  /** Leading glyph (`›` user, `◆` assistant, `·` system). */
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
      glyph: symbols.prompt,
      glyphColor: colors.dim,
      label: "",
      labelColor: "",
      textColor: colors.bright,
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
