/**
 * The CLI theme registry — all seven dashboard palettes, hand-copied
 * from `dashboard/src/app/theme/themes.ts`. The two TS projects are
 * separate packages; importing across would couple them, so the hexes
 * live here and `themes.test.ts` pins the port against drift.
 *
 * A **theme** is a complete palette with a dark and a light reading;
 * the CLI picks one with `<id>-<dark|light>` (e.g. `graphite-light`).
 * Ink 5 paints chrome as filled `Box`/`Text` backgrounds, so the
 * dashboard's planes (floor/lane/rail/raised) ship as hexes alongside
 * the eight text/status primitives.
 *
 * Token mapping, identical for every theme:
 *
 * | CLI token | dashboard primitive |
 * |-----------|---------------------|
 * | floor     | floor               |
 * | lane      | lane                |
 * | rail      | rail                |
 * | raised    | raised              |
 * | text      | text                |
 * | dim, muted| muted               |
 * | faint     | faint               |
 * | accent    | status-running      |
 * | ok        | status-success      |
 * | error     | status-failed       |
 * | waiting   | status-waiting      |
 * | rule      | rule                |
 *
 * The accent choice is deliberate: every dashboard `accent` is a
 * filled-button plane (bone on dark, ink on light) — a surface, not a
 * hue — while `status-running` is the palette's own living signal,
 * which is exactly what the CLI's brand mark, spinner and streaming
 * cursor are. Per-theme notes below record what that buys.
 */

/** The dashboard primitives one CLI theme carries, as source hexes. */
export interface CliThemePrimitives {
  /** Empty canvas — the shell floor. */
  readonly floor: string
  /** User/assistant cards, tab strip. */
  readonly lane: string
  /** Status + footer bands. */
  readonly rail: string
  /** Prompt well. */
  readonly raised: string
  /** Body text. */
  readonly text: string
  /** Secondary text — the CLI's `dim` and `muted` (one tier, two names). */
  readonly muted: string
  /** Tertiary text — notices and background noise. */
  readonly faint: string
  /** The hairline (plan-card frames, code-block borders). */
  readonly rule: string
  /** status-running — the CLI accent. */
  readonly running: string
  /** status-success — the CLI `ok`. */
  readonly success: string
  /** status-failed — the CLI `error`. */
  readonly failed: string
  /** status-waiting — the CLI `waiting`. */
  readonly waiting: string
}

export interface CliTheme {
  /** Short id used in `--theme <id>-<mode>` (dashboard `dichromat-deck` → `dichromat`). */
  readonly id: string
  /** What a person sees in the picker. */
  readonly name: string
  /** The two renderings. Both required — a theme without one is a mode. */
  readonly dark: CliThemePrimitives
  readonly light: CliThemePrimitives
}

export type ThemeMode = "dark" | "light"

/*
 * Dichromat deck — the committed default. Accent ← running #8787f3:
 * the deck's own chrome accent is bone/ink (a filled-button plane),
 * and running's periwinkle is the palette's only living hue — the one
  * the CLI's ASCII brand mark already wore. `dichromat-dark` is
 * byte-identical to the palette the CLI shipped before themes.
 */
const DICHROMAT: CliTheme = {
  id: "dichromat",
  name: "Dichromat deck",
  dark: {
    floor: "#222226",
    lane: "#26262b",
    rail: "#2b2b30",
    raised: "#313136",
    text: "#e8e8ee",
    muted: "#b8b8bd",
    faint: "#8a8a8f",
    rule: "#37373c",
    running: "#8787f3",
    success: "#d7d7ff",
    failed: "#d2d228",
    waiting: "#b4b442",
  },
  light: {
    floor: "#e9e9f0",
    lane: "#f5f5fb",
    rail: "#e3e3e9",
    raised: "#ffffff",
    text: "#1f1f24",
    muted: "#414147",
    faint: "#67676c",
    rule: "#d7d7dd",
    running: "#5353d7",
    success: "#0d0d7d",
    failed: "#333311",
    waiting: "#4d4d01",
  },
}

/*
 * Graphite. Accent ← running #6cb2ff (dark) / #00579e (light): the
 * cold sky-blue the board uses for live work — the theme's cyan
 * chrome accent (#50ccd9) is a button plane and would fight the
 * status ladder's blues.
 */
const GRAPHITE: CliTheme = {
  id: "graphite",
  name: "Graphite",
  dark: {
    floor: "#0d0f13",
    lane: "#14171a",
    rail: "#1a1d20",
    raised: "#1f2326",
    text: "#e4e8ed",
    muted: "#adb1b6",
    faint: "#7a7d81",
    rule: "#25292c",
    running: "#6cb2ff",
    success: "#75d0ae",
    failed: "#f77671",
    waiting: "#ffdaac",
  },
  light: {
    floor: "#f3f7fc",
    lane: "#fcfeff",
    rail: "#e8edf2",
    raised: "#feffff",
    text: "#1c2023",
    muted: "#42464a",
    faint: "#6c7073",
    rule: "#dfe3e8",
    running: "#00579e",
    success: "#006f53",
    failed: "#951720",
    waiting: "#4f3000",
  },
}

/*
 * Dockside. Accent ← running #83aefe (dark) / #2e549c (light): the
 * umber room keeps copper (#f9a163) for its buttons, but copper is
 * one step from the failed terracotta — running's distant blue keeps
 * the spinner unmistakably alive.
 */
const DOCKSIDE: CliTheme = {
  id: "dockside",
  name: "Dockside",
  dark: {
    floor: "#1c1209",
    lane: "#22170e",
    rail: "#2e1e0f",
    raised: "#31261c",
    text: "#ebe5df",
    muted: "#b7b0aa",
    faint: "#867e76",
    rule: "#352a20",
    running: "#83aefe",
    success: "#99cb8e",
    failed: "#f47b61",
    waiting: "#ffd9b2",
  },
  light: {
    floor: "#fdefe1",
    lane: "#fffaf5",
    rail: "#f4dfcb",
    raised: "#fffdfc",
    text: "#25211c",
    muted: "#4a443e",
    faint: "#736b63",
    rule: "#e6d7ca",
    running: "#2e549c",
    success: "#3d6b33",
    failed: "#921e05",
    waiting: "#512f00",
  },
}

/*
 * Blueprint. Accent ← running #9bd6f6 (dark) / #005799 (light): on a
 * board whose chrome is already cyanotype blue, running is the
 * paper's own working ink — the ice-white accent (#b2e8fa) would
 * vanish against text on a terminal floor.
 */
const BLUEPRINT: CliTheme = {
  id: "blueprint",
  name: "Blueprint",
  dark: {
    floor: "#061629",
    lane: "#0c1b2f",
    rail: "#0b213b",
    raised: "#17273c",
    text: "#e1e6ec",
    muted: "#abb3bc",
    faint: "#78828e",
    rule: "#1c2d42",
    running: "#9bd6f6",
    success: "#62d1ae",
    failed: "#fc7460",
    waiting: "#ffd9ac",
  },
  light: {
    floor: "#e7f2ff",
    lane: "#f7fbff",
    rail: "#d4e7ff",
    raised: "#ffffff",
    text: "#1a1d22",
    muted: "#3e4349",
    faint: "#676d75",
    rule: "#c7dcf8",
    running: "#005799",
    success: "#006f55",
    failed: "#980e04",
    waiting: "#4f3100",
  },
}

/*
 * Bureau. Accent ← running #66b3ff (dark) / #005899 (light): the
 * stamp-violet accent (#da90b3) belongs to a document's seal, not to
 * a signal — running's archival blue is what the bureau marks live
 * work with.
 */
const BUREAU: CliTheme = {
  id: "bureau",
  name: "Bureau",
  dark: {
    floor: "#1b1a18",
    lane: "#1e1e1b",
    rail: "#22221f",
    raised: "#272624",
    text: "#e6e6e2",
    muted: "#b4b4b0",
    faint: "#858481",
    rule: "#2b2b28",
    running: "#66b3ff",
    success: "#89ce92",
    failed: "#f77769",
    waiting: "#ffdba3",
  },
  light: {
    floor: "#f3f3ef",
    lane: "#fffffe",
    rail: "#eaeae6",
    raised: "#fffffe",
    text: "#1e1e1b",
    muted: "#444441",
    faint: "#6e6e6a",
    rule: "#e0dfdc",
    running: "#005899",
    success: "#286e37",
    failed: "#951815",
    waiting: "#4b3200",
  },
}

/*
 * Aperture. Accent ← running #76b0ff (dark) / #0954a8 (light): depth
 * inversion makes the chrome mid-grey and the data near-black, so the
 * lime accent (#c1d75b) reads as furniture; running's blue is the
 * light that comes through the window.
 */
const APERTURE: CliTheme = {
  id: "aperture",
  name: "Aperture",
  dark: {
    floor: "#282d31",
    lane: "#0e1215",
    rail: "#2f3337",
    raised: "#35393e",
    text: "#e9eef4",
    muted: "#bcc1c6",
    faint: "#90959a",
    rule: "#2c3135",
    running: "#76b0ff",
    success: "#71d19c",
    failed: "#ff7166",
    waiting: "#ffdaa6",
  },
  light: {
    floor: "#d5dae0",
    lane: "#feffff",
    rail: "#e0e6eb",
    raised: "#feffff",
    text: "#1a1d22",
    muted: "#393c42",
    faint: "#5b5e64",
    rule: "#c7ccd2",
    running: "#0954a8",
    success: "#006f44",
    failed: "#9b040f",
    waiting: "#4c3100",
  },
}

/*
 * Dispatcher — the legacy board, kept as evidence. Accent ← running
 * #3c5a86: the original's steel blue, deliberately NOT the turquoise
 * (#4fb3ac) — running on this board is muted enough that the CLI
 * spinner stays legible on it, while turquoise sits one hue from the
 * board's whole identity and would read as decoration.
 */
const DISPATCHER: CliTheme = {
  id: "dispatcher",
  name: "Dispatcher",
  dark: {
    floor: "#0c0f13",
    lane: "#11151a",
    rail: "#171c24",
    raised: "#1a1f27",
    text: "#dee4ea",
    muted: "#aaafb4",
    faint: "#787d82",
    rule: "#1d232a",
    running: "#3c5a86",
    success: "#4e7c5b",
    failed: "#b0473b",
    waiting: "#9c7a3c",
  },
  light: {
    floor: "#fbfbfa",
    lane: "#f1f2ef",
    rail: "#f0f1ee",
    raised: "#ffffff",
    text: "#1b232e",
    muted: "#434952",
    faint: "#6e7379",
    rule: "#e6e7e4",
    running: "#3c5a86",
    success: "#4e7c5b",
    failed: "#b0473b",
    waiting: "#9c7a3c",
  },
}

/** Every theme, in the order the dashboard picker lists them. */
export const CLI_THEMES: readonly CliTheme[] = [
  DICHROMAT,
  GRAPHITE,
  DOCKSIDE,
  BLUEPRINT,
  BUREAU,
  APERTURE,
  DISPATCHER,
]

/** The choice used when neither flag nor config names one. */
export const DEFAULT_THEME_CHOICE = "dichromat-dark"

/** One resolved `<id>-<mode>` pair. */
export interface ThemeChoice {
  readonly theme: CliTheme
  readonly mode: ThemeMode
}

/** Parses `<id>-<dark|light>`; undefined for anything unrecognised. */
export function findCliThemeChoice(value: string): ThemeChoice | undefined {
  const separator = value.lastIndexOf("-")
  if (separator <= 0) {
    return undefined
  }
  const themeId = value.slice(0, separator)
  const mode = value.slice(separator + 1)
  if (mode !== "dark" && mode !== "light") {
    return undefined
  }
  const theme = CLI_THEMES.find((candidate) => candidate.id === themeId)
  return theme ? { theme, mode } : undefined
}

/** All 14 valid `--theme` values, registry order. */
export const THEME_CHOICE_IDS: readonly string[] = CLI_THEMES.flatMap(
  (theme) => [`${theme.id}-dark`, `${theme.id}-light`]
)

/** Type guard for untrusted input (flag, config file). */
export function isThemeChoice(value: unknown): value is string {
  return typeof value === "string" && findCliThemeChoice(value) !== undefined
}
