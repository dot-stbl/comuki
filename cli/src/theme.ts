/**
 * The one accent palette of the terminal-native UI (minimal structure,
 * Comuki brand colour): no boxes, no borders — the terminal IS the
 * chrome, hierarchy comes from spacing, weight and the deck's
 * restrained status colours. Raw ANSI escapes instead of Ink
 * `<Text color>` so the pure formatters in `lib/format.ts` can build
 * finished strings without React.
 *
 * Source of truth: DESIGN.md — the **Dichromat deck, dark reading**
 * (the dashboard's committed default theme), which is also this
 * module's initial state and the CLI theme default. Truecolor escapes
 * (`38;2;R;G;B`) because the deck's primitives have no 256-cube
 * equivalents worth snapping to. Dichromat discipline: colour never
 * carries status alone — every status pairs with its word (`ok`,
 * `error`), and success=lavender / failed=yellow is deliberate.
 *
 * Themes: `themes.ts` ports all seven dashboard palettes (each with a
 * dark and a light reading). `resolveTheme(choice)` picks one
 * (`<theme>-<dark|light>`, default `dichromat-dark`), installs it into
 * the exported `colors` / `palette` objects and returns the tokens.
 * The install is a one-time boot step (`index.tsx`, before the first
 * render): every pure formatter reads `colors.*` at call time, so a
 * theme swap is one call with no React plumbing — the one deliberate
 * module-state mutation in the CLI.
 */

import {
  DEFAULT_THEME_CHOICE,
  findCliThemeChoice,
  type CliTheme,
  type ThemeMode,
} from "./themes"

/** Ink-facing hex tokens — `<Text color>` props cannot take ANSI codes. */
export interface PaletteTokens {
  /** The accent hex — the ◆ brand mark, spinner, streaming cursor. */
  brand: string
  /** Deck text — bold reading text. */
  text: string
  /** status-success hex. */
  ok: string
  /** status-failed hex. */
  error: string
  /** status-waiting hex. */
  waiting: string
}

export const palette: PaletteTokens = {
  brand: "#8787f3",
  text: "#e8e8ee",
  ok: "#d7d7ff",
  error: "#d2d228",
  waiting: "#b4b442",
}

/** ANSI SGR escape tokens — the eight theme colors plus fixed style flags. */
export interface AnsiTokens {
  /** deck `text` — reading text on dark. */
  text: string
  /** deck `text-muted` — the quiet tier: bullets, labels, meta. */
  dim: string
  /** deck `text-muted` again — `dim` and `muted` are one tier, two names. */
  muted: string
  /** deck `text-faint` — notices and other background noise. */
  faint: string
  /** deck `status-running` — accent, spinner, streaming cursor. */
  accent: string
  /** deck `status-success` — pale lavender, never green. */
  ok: string
  /** deck `status-failed` — yellow, never red. */
  error: string
  /** deck `status-waiting`. */
  waiting: string
  /** deck `rule` — frame lines (plan card, code-block borders). */
  rule: string
  bright: string
  italic: string
  strike: string
  underline: string
  reset: string
}

export const colors: AnsiTokens = {
  text: "\x1b[38;2;232;232;238m",
  dim: "\x1b[38;2;184;184;189m",
  muted: "\x1b[38;2;184;184;189m",
  faint: "\x1b[38;2;138;138;143m",
  accent: "\x1b[38;2;135;135;243m",
  ok: "\x1b[38;2;215;215;255m",
  error: "\x1b[38;2;210;210;40m",
  waiting: "\x1b[38;2;180;180;66m",
  rule: "\x1b[38;2;55;55;60m",
  bright: "\x1b[1m",
  italic: "\x1b[3m",
  strike: "\x1b[9m",
  underline: "\x1b[4m",
  reset: "\x1b[0m",
}

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

// ---------------------------------------------------------------------------
// Theme resolution
// ---------------------------------------------------------------------------

/** The eight colors a theme carries, as source hexes (`#rrggbb`). */
export interface ThemeHex {
  readonly text: string
  readonly dim: string
  readonly faint: string
  readonly accent: string
  readonly ok: string
  readonly error: string
  readonly waiting: string
  readonly rule: string
}

/** One resolved, installed theme — hexes plus the truecolor escapes. */
export interface ThemeTokens {
  /** The full choice id: `<theme>-<mode>`. */
  readonly id: string
  readonly themeId: string
  readonly name: string
  readonly mode: ThemeMode
  readonly hex: ThemeHex
  readonly ansi: ThemeHex
}

/** `#rrggbb` → `\x1b[38;2;R;G;Bm`; a malformed hex throws (registry typo). */
function hexToTruecolor(hex: string): string {
  const match = /^#([0-9a-f]{6})$/i.exec(hex)
  if (!match) {
    throw new Error(`bad theme hex: ${hex}`)
  }
  const value = Number.parseInt(match[1], 16)
  return `\x1b[38;2;${(value >> 16) & 0xff};${(value >> 8) & 0xff};${value & 0xff}m`
}

function themeHex(
  primitives: Readonly<
    Record<"text" | "muted" | "faint" | "rule" | "running" | "success" | "failed" | "waiting", string>
  >
): ThemeHex {
  return {
    text: primitives.text,
    dim: primitives.muted,
    faint: primitives.faint,
    accent: primitives.running,
    ok: primitives.success,
    error: primitives.failed,
    waiting: primitives.waiting,
    rule: primitives.rule,
  }
}

/** Last `resolveTheme` install — `/theme` listing reads this. */
let installedThemeId = DEFAULT_THEME_CHOICE

/** The currently installed `<id>-<mode>` choice. */
export function currentThemeId(): string {
  return installedThemeId
}

function install(theme: CliTheme, mode: ThemeMode): ThemeTokens {
  const primitives = mode === "dark" ? theme.dark : theme.light
  const hex = themeHex(primitives)
  const ansi: Record<keyof ThemeHex, string> = {
    text: hexToTruecolor(hex.text),
    dim: hexToTruecolor(hex.dim),
    faint: hexToTruecolor(hex.faint),
    accent: hexToTruecolor(hex.accent),
    ok: hexToTruecolor(hex.ok),
    error: hexToTruecolor(hex.error),
    waiting: hexToTruecolor(hex.waiting),
    rule: hexToTruecolor(hex.rule),
  }
  colors.text = ansi.text
  colors.dim = ansi.dim
  colors.muted = ansi.dim
  colors.faint = ansi.faint
  colors.accent = ansi.accent
  colors.ok = ansi.ok
  colors.error = ansi.error
  colors.waiting = ansi.waiting
  colors.rule = ansi.rule
  palette.brand = hex.accent
  palette.text = hex.text
  palette.ok = hex.ok
  palette.error = hex.error
  palette.waiting = hex.waiting
  installedThemeId = `${theme.id}-${mode}`
  return {
    id: `${theme.id}-${mode}`,
    themeId: theme.id,
    name: theme.name,
    mode,
    hex,
    ansi,
  }
}

/**
 * Resolves `choice` (`<theme>-<dark|light>`) and installs it as the
 * active palette; `undefined` or an unrecognised value falls back to
 * `dichromat-dark`. Call once at boot before the first render — the
 * returned tokens are for display (`--theme` echo, tests); everything
 * downstream reads the installed `colors` / `palette`.
 */
export function resolveTheme(choice?: string): ThemeTokens {
  const found =
    (choice === undefined ? undefined : findCliThemeChoice(choice)) ??
    findCliThemeChoice(DEFAULT_THEME_CHOICE)
  if (!found) {
    // Unreachable while the registry holds DEFAULT_THEME_CHOICE.
    throw new Error(`broken theme registry: ${DEFAULT_THEME_CHOICE} did not resolve`)
  }
  return install(found.theme, found.mode)
}

export {
  CLI_THEMES,
  DEFAULT_THEME_CHOICE,
  THEME_CHOICE_IDS,
  isThemeChoice,
} from "./themes"
export type { CliTheme, CliThemePrimitives, ThemeMode } from "./themes"

// ---------------------------------------------------------------------------
// Paint helpers
// ---------------------------------------------------------------------------

/** Wraps `text` in an ANSI color, resetting after. */
export function paint(text: string, color: string): string {
  return color + text + colors.reset
}

/** Strips ANSI escapes — SGR colors and OSC sequences (hyperlinks) — used by width-aware truncation and tests. */
export function stripAnsi(text: string): string {
  // eslint-disable-next-line no-control-regex
  return text
    .replace(/\x1b\[[0-9;]*m/g, "")
    .replace(/\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)/g, "")
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
