/**
 * The one accent palette of the terminal-native UI (option A, owner
 * approved): no boxes, no borders — the terminal IS the chrome. Raw ANSI
 * escapes instead of Ink `<Text color>` so the pure formatters in
 * `lib/format.ts` can build finished strings without React.
 *
 * One accent (slate-blue) for statuses and the prompt; thinking is dimmed
 * gray, tools muted mono, results in the terminal's default color.
 */
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
  spinnerFrames: ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"],
} as const

/** Wraps `text` in an ANSI color, resetting after. */
export function paint(text: string, color: string): string {
  return color + text + colors.reset
}

/** Strips ANSI escapes — used by width-aware truncation and tests. */
export function stripAnsi(text: string): string {
  // eslint-disable-next-line no-control-regex
  return text.replace(/\x1b\[[0-9;]*m/g, "")
}
