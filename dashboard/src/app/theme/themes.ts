/* The theme registry.
 *
 * Two axes, not one. A **theme** is a complete palette that has both a dark and
 * a light rendering; a **mode** (`dark | light | system`) says which of the two
 * is showing. A person picks one of each, independently, and neither choice
 * changes the list the other offers. Collapsing them would double this list for
 * nothing and would make "light" a different *theme* from "dark", which is not
 * what either word means.
 *
 * Every theme declares the same primitives, and `theme-css.ts` turns those
 * primitives into the same forty-odd semantic tokens. Screens read
 * `--background`, `--rail`, `--st-failed`, `--brand`, `--text-faint`; a theme
 * changes their *values* and never their vocabulary. `palette.test.ts` holds
 * that promise, along with the contrast and colour-blindness floors below.
 *
 * The five directions after the default are the palette review's own, imported
 * verbatim — their ladders were computed once and re-deriving them here would
 * only introduce drift.
 */

/** The primitives a theme has to state. Everything else is derived from these. */
export interface Palette {
  /** The working plane the whole board sits on. */
  floor: string
  /** The rail's own plane — one visible step off the floor, never below it. */
  rail: string
  /** A data row. */
  lane: string
  /** The banded row beside it, and the surface a hover darkens toward in light. */
  laneAlt: string
  /** Popovers, menus, dialogs: the one surface that is genuinely in front. */
  raised: string
  /** The hover/selected chrome surface (shadcn's `--accent`). */
  hover: string
  /** The hairline. */
  rule: string
  /** The hairline that has to be seen. */
  ruleStrong: string
  /** Body text. */
  text: string
  /** Secondary text. */
  muted: string
  /** Tertiary text — still content, so it still clears 4.5:1 on the floor. */
  faint: string
  /** The one accent: filled button, focus ring, active rail border, caret. */
  accent: string
  /** What is legible *on* the accent. */
  accentFg: string
  /** Destructive controls. */
  destructive: string

  /* The six real run statuses. No invented vocabulary, and no seventh. */
  running: string
  queued: string
  waiting: string
  escalated: string
  failed: string
  success: string
}

/**
 * ## The three text tiers, and why they are placed rather than picked
 *
 * `text` · `muted` · `faint` are a ladder, and they were not one. Every theme
 * had shipped with a huge step from body text down to muted and a hair from
 * muted to faint — 23 and 7 in the default's dark mode, 27 and 4 in its light.
 * Two tiers that close means one tier: a screen written in muted and faint read
 * as a single quiet voice, and the third level of hierarchy did nothing.
 *
 * Both are now derived, not chosen. `faint` is the quietest step that still
 * clears 4.5:1 on the floor — that is its whole definition, the dimmest legible
 * thing. `muted` sits at the midpoint in lightness between `faint` and `text`.
 * The two gaps come out equal by construction, 15 to 20 in every theme and
 * mode, and they scale with the room a theme actually has rather than being
 * copied between palettes that do not share a floor.
 *
 * Both are mixed along the theme's own floor-to-text ramp, which is what keeps
 * the default on the plane where the dichromatic projections are fixed points:
 * mixing is per-channel, so two colours with equal red and green produce a
 * third with equal red and green, exactly.
 */
export interface Theme {
  /** Stable id. Written to `data-theme` on the root and to storage. */
  id: string
  /** What a person sees in the picker. */
  name: string
  /** One line: what this theme is *for*. */
  note: string
  /** The two renderings. Both are required — a theme without one is a mode. */
  palette: { light: Palette; dark: Palette }
  /**
   * A palette kept as a record rather than held to the current bar.
   *
   * Exactly one theme is allowed this, and it buys nothing quietly: the
   * contrast and separation floors skip it, and `palette.test.ts` asserts in
   * their place the precise ways it falls short. Repair one of them and the
   * test fails — the exemption records a defect, it does not excuse it.
   */
  legacy?: true
}

/* ------------------------------------------------------------------ *
 * The default.
 *
 * `deck`, projected into a protanope's colour space and then adopted as the
 * real thing rather than as an accommodation.
 *
 * The Viénot–Brettel–Mollon projection maps every colour onto the plane where
 * linear red equals linear green. A colour already on that plane is a fixed
 * point: simulate protanopia or deuteranopia and *nothing moves*. So a palette
 * built there looks identical to a normal-sighted reader and to a dichromat —
 * safe by construction, not by compensation. `palette.test.ts` asserts the
 * fixed-point property literally, hex by hex.
 *
 * The chrome is the projection of `deck`, taken verbatim; it was already on the
 * plane, because everything the projection outputs is. The accent is deck's own
 * idea — bone on dark, ink on light, a filled button rather than a hue — and it
 * is what leaves the chrome colourless.
 *
 * The six statuses could *not* be taken from the projection. Six hues squeezed
 * into two dimensions collapse: `failed` and `success` both landed on olive,
 * 1.2 L* apart from their neighbours. So they were rebuilt inside the plane
 * instead, on two axes and only two — lightness, and the blue↔yellow offset
 * that survives. The ladder is six rungs about 5.3 L* apart in dark and 5.6 in
 * light, and consecutive rungs alternate warm and cool so that no two statuses
 * ever share both a rung and a direction.
 *
 * `running` and `success` get the widest berth of the six — 26 L* and ΔEok 0.24
 * apart — because they are the only two statuses with `--weave-*: none`. Every
 * other status carries a hatch, so hue is its second channel rather than its
 * only one; those two are separated by colour alone and are placed at opposite
 * ends of the ladder for it.
 *
 * `destructive` is `failed`'s rung at the warm edge of the plane: the same
 * alarm in its imperative mood. In dark that is visibly hotter than `failed`
 * (a pure yellow against a tempered one); in light the plane is only ~50 units
 * wide at that lightness and the two end up close. That is a property of the
 * gamut, not an oversight — there is no second red in a world without red.
 * ------------------------------------------------------------------ */
const DICHROMAT_DECK: Theme = {
  id: "dichromat-deck",
  name: "Dichromat deck",
  note: "Deck as a protanope sees it, kept that way — colourless chrome, and statuses built on the one axis red-green blindness leaves standing.",
  palette: {
    dark: {
      floor: "#222226",
      rail: "#2b2b30",
      lane: "#26262b",
      laneAlt: "#232327",
      raised: "#313136",
      hover: "#313136",
      rule: "#37373c",
      ruleStrong: "#47474c",
      text: "#e8e8ee",
      muted: "#b8b8bd",
      faint: "#8a8a8f",
      accent: "#ebebf0",
      accentFg: "#1c1c21",
      destructive: "#d2d200",
      running: "#8787f3",
      queued: "#a2a28a",
      waiting: "#b4b442",
      escalated: "#b7b7fd",
      failed: "#d2d228",
      success: "#d7d7ff",
    },
    light: {
      floor: "#e9e9f0",
      rail: "#e3e3e9",
      lane: "#f5f5fb",
      laneAlt: "#e8e8ee",
      raised: "#ffffff",
      hover: "#e8e8ee",
      rule: "#d7d7dd",
      ruleStrong: "#bdbdc4",
      text: "#1f1f24",
      muted: "#414147",
      faint: "#67676c",
      accent: "#2c2c31",
      accentFg: "#f6f6ff",
      destructive: "#333300",
      running: "#5353d7",
      queued: "#595949",
      waiting: "#4d4d01",
      escalated: "#2424b0",
      failed: "#333311",
      success: "#0d0d7d",
    },
  },
}

const GRAPHITE: Theme = {
  id: "graphite",
  name: "Graphite",
  note: "The colourless rule executed properly — cold near-black, with wide enough steps between floor, lane and rail that the chrome reads as structure rather than as dirt.",
  palette: {
    dark: {
      floor: "#0d0f13",
      rail: "#1a1d20",
      lane: "#14171a",
      laneAlt: "#101317",
      raised: "#1f2326",
      hover: "#1f2326",
      rule: "#25292c",
      ruleStrong: "#34373b",
      text: "#e4e8ed",
      muted: "#adb1b6",
      faint: "#7a7d81",
      accent: "#50ccd9",
      accentFg: "#141d1e",
      destructive: "#f77671",
      running: "#6cb2ff",
      queued: "#888f95",
      waiting: "#ffdaac",
      escalated: "#d9c6ff",
      failed: "#f77671",
      success: "#75d0ae",
    },
    light: {
      floor: "#f3f7fc",
      rail: "#e8edf2",
      lane: "#fcfeff",
      laneAlt: "#eef2f7",
      raised: "#feffff",
      hover: "#eef2f7",
      rule: "#dfe3e8",
      ruleStrong: "#c8cdd1",
      text: "#1c2023",
      muted: "#42464a",
      faint: "#6c7073",
      accent: "#00737d",
      accentFg: "#edf8fa",
      destructive: "#951720",
      running: "#00579e",
      queued: "#676d73",
      waiting: "#4f3000",
      escalated: "#512f7e",
      failed: "#951720",
      success: "#006f53",
    },
  },
}

const DOCKSIDE: Theme = {
  id: "dockside",
  name: "Dockside",
  note: "A warm room for a long shift — rail, topbar and lanes carry a real umber tint instead of being drained to neutral, and the filled button is copper.",
  palette: {
    dark: {
      floor: "#1c1209",
      rail: "#2e1e0f",
      lane: "#22170e",
      laneAlt: "#1e150b",
      raised: "#31261c",
      hover: "#31261c",
      rule: "#352a20",
      ruleStrong: "#453a2f",
      text: "#ebe5df",
      muted: "#b7b0aa",
      faint: "#867e76",
      accent: "#f9a163",
      accentFg: "#201a17",
      destructive: "#f47b61",
      running: "#83aefe",
      queued: "#968d85",
      waiting: "#ffd9b2",
      escalated: "#ebbfff",
      failed: "#f47b61",
      success: "#99cb8e",
    },
    light: {
      floor: "#fdefe1",
      rail: "#f4dfcb",
      lane: "#fffaf5",
      laneAlt: "#f8eadc",
      raised: "#fffdfc",
      hover: "#f8eadc",
      rule: "#e6d7ca",
      ruleStrong: "#cdbfb2",
      text: "#25211c",
      muted: "#4a443e",
      faint: "#736b63",
      accent: "#a35623",
      accentFg: "#fef4f0",
      destructive: "#921e05",
      running: "#2e549c",
      queued: "#736a63",
      waiting: "#512f00",
      escalated: "#5d2b72",
      failed: "#921e05",
      success: "#3d6b33",
    },
  },
}

const BLUEPRINT: Theme = {
  id: "blueprint",
  name: "Blueprint",
  note: "The board as a cyanotype — the chrome is blue, so the plane the operator reads on is a material rather than an absence, and the accent is the paper's own ice-white.",
  palette: {
    dark: {
      floor: "#061629",
      rail: "#0b213b",
      lane: "#0c1b2f",
      laneAlt: "#08182b",
      raised: "#17273c",
      hover: "#17273c",
      rule: "#1c2d42",
      ruleStrong: "#2d3f55",
      text: "#e1e6ec",
      muted: "#abb3bc",
      faint: "#78828e",
      accent: "#b2e8fa",
      accentFg: "#151d1f",
      destructive: "#fc7460",
      running: "#9bd6f6",
      queued: "#948d87",
      waiting: "#ffd9ac",
      escalated: "#cb99f7",
      failed: "#fc7460",
      success: "#62d1ae",
    },
    light: {
      floor: "#e7f2ff",
      rail: "#d4e7ff",
      lane: "#f7fbff",
      laneAlt: "#e0eeff",
      raised: "#ffffff",
      hover: "#e0eeff",
      rule: "#c7dcf8",
      ruleStrong: "#aec3de",
      text: "#1a1d22",
      muted: "#3e4349",
      faint: "#676d75",
      accent: "#07597c",
      accentFg: "#eff7fd",
      destructive: "#980e04",
      running: "#005799",
      queued: "#716b65",
      waiting: "#4f3100",
      escalated: "#5a297e",
      failed: "#980e04",
      success: "#006f55",
    },
  },
}

const BUREAU: Theme = {
  id: "bureau",
  name: "Bureau",
  note: "Built light first and darkened afterwards — paper chrome, ink text and a stamp-violet accent that belongs to a document rather than to a screen.",
  palette: {
    dark: {
      floor: "#1b1a18",
      rail: "#22221f",
      lane: "#1e1e1b",
      laneAlt: "#191816",
      raised: "#272624",
      hover: "#272624",
      rule: "#2b2b28",
      ruleStrong: "#3a3936",
      text: "#e6e6e2",
      muted: "#b4b4b0",
      faint: "#858481",
      accent: "#da90b3",
      accentFg: "#201a1c",
      destructive: "#f77769",
      running: "#66b3ff",
      queued: "#8a8e93",
      waiting: "#ffdba3",
      escalated: "#dbc5ff",
      failed: "#f77769",
      success: "#89ce92",
    },
    light: {
      floor: "#f3f3ef",
      rail: "#eaeae6",
      lane: "#fffffe",
      laneAlt: "#f9f8f4",
      raised: "#fffffe",
      hover: "#f9f8f4",
      rule: "#e0dfdc",
      ruleStrong: "#c7c6c3",
      text: "#1e1e1b",
      muted: "#444441",
      faint: "#6e6e6a",
      accent: "#8c3564",
      accentFg: "#fdf4f8",
      destructive: "#951815",
      running: "#005899",
      queued: "#696c70",
      waiting: "#4b3200",
      escalated: "#532e7d",
      failed: "#951815",
      success: "#286e37",
    },
  },
}

const APERTURE: Theme = {
  id: "aperture",
  name: "Aperture",
  note: "Depth inverted — the chrome comes up to a mid grey and the data drops to near-black, so the table reads as a window cut in the wall rather than a panel laid on it.",
  palette: {
    dark: {
      floor: "#282d31",
      rail: "#2f3337",
      lane: "#0e1215",
      laneAlt: "#0a0d11",
      raised: "#35393e",
      hover: "#35393e",
      rule: "#2c3135",
      ruleStrong: "#43484c",
      text: "#e9eef4",
      muted: "#bcc1c6",
      faint: "#90959a",
      accent: "#c1d75b",
      accentFg: "#1b1c16",
      destructive: "#ff7166",
      running: "#76b0ff",
      queued: "#878f96",
      waiting: "#ffdaa6",
      escalated: "#dec3ff",
      failed: "#ff7166",
      success: "#71d19c",
    },
    light: {
      floor: "#d5dae0",
      rail: "#e0e6eb",
      lane: "#feffff",
      laneAlt: "#f4faff",
      raised: "#feffff",
      hover: "#f4faff",
      rule: "#c7ccd2",
      ruleStrong: "#aeb4b9",
      text: "#1a1d22",
      muted: "#393c42",
      faint: "#5b5e64",
      accent: "#5c6900",
      accentFg: "#f5f7ef",
      destructive: "#9b040f",
      running: "#0954a8",
      queued: "#666c74",
      waiting: "#4c3100",
      escalated: "#582982",
      failed: "#9b040f",
      success: "#006f44",
    },
  },
}

/** Every theme, in the order the picker lists them. The default comes first. */
/* ------------------------------------------------------------------ *
 * The board this product wore before the palette review.
 *
 * Kept as a theme rather than deleted, because a palette that shipped is
 * evidence: it is the only one of the seven anybody has actually read a shift
 * on, and comparing against it is worth more than a memory of it.
 *
 * A reconstruction, and honestly labelled as one. Eleven of its values were
 * read straight off the sheet it lived in; five neutrals (both `raised`, both
 * `hover`, both `faint`) were never recorded before the sheet was rewritten and
 * are placed on the ramp the recorded ones define rather than picked by eye —
 * `raised` between rail and rule, `hover` half a step off the lane, and
 * `faint` walked up from the strong rule until it clears 4.5:1 on the floor
 * rather than sat at a pleasing midpoint that did not.
 *
 * It is the one theme here that fails the fixed-point property: its statuses
 * are a hue set, so `failed` and `success` are a red and a green and they do
 * collapse under protanopia. That is not a defect to fix — it is what the
 * default was built to answer, and keeping the question visible beside the
 * answer is the reason this stays in the list.
 * ------------------------------------------------------------------ */
const DISPATCHER: Theme = {
  id: "dispatcher",
  name: "Dispatcher",
  legacy: true,
  note: "The original board — near-black floor, colourless chrome, one turquoise accent, and six status hues. The palette every earlier decision in this product was made against.",
  palette: {
    dark: {
      floor: "#0c0f13",
      rail: "#171c24",
      lane: "#11151a",
      laneAlt: "#0e1216",
      raised: "#1a1f27",
      hover: "#14191f",
      rule: "#1d232a",
      ruleStrong: "#2b333b",
      text: "#dee4ea",
      muted: "#aaafb4",
      faint: "#787d82",
      accent: "#4fb3ac",
      accentFg: "#0b0e12",
      destructive: "#e0705f",
      running: "#3c5a86",
      queued: "#a0a4ac",
      waiting: "#9c7a3c",
      escalated: "#6e5ba6",
      failed: "#b0473b",
      success: "#4e7c5b",
    },
    light: {
      floor: "#fbfbfa",
      rail: "#f0f1ee",
      lane: "#f1f2ef",
      laneAlt: "#f7f8f6",
      raised: "#ffffff",
      hover: "#ecedea",
      rule: "#e6e7e4",
      ruleStrong: "#d2d4d0",
      text: "#1b232e",
      muted: "#434952",
      faint: "#6e7379",
      accent: "#24706b",
      accentFg: "#ffffff",
      destructive: "#b0473b",
      running: "#3c5a86",
      queued: "#a0a4ac",
      waiting: "#9c7a3c",
      escalated: "#6e5ba6",
      failed: "#b0473b",
      success: "#4e7c5b",
    },
  },
}

export const THEMES: readonly Theme[] = [
  DICHROMAT_DECK,
  GRAPHITE,
  DOCKSIDE,
  BLUEPRINT,
  BUREAU,
  APERTURE,
  DISPATCHER,
]

export const DEFAULT_THEME_ID = DICHROMAT_DECK.id

/** The keys of `Palette`, in the order `theme-css.ts` emits them. */
export const PALETTE_KEYS = [
  "floor",
  "rail",
  "lane",
  "laneAlt",
  "raised",
  "hover",
  "rule",
  "ruleStrong",
  "text",
  "muted",
  "faint",
  "accent",
  "accentFg",
  "destructive",
  "running",
  "queued",
  "waiting",
  "escalated",
  "failed",
  "success",
] as const satisfies readonly (keyof Palette)[]

/** The six statuses, in ladder order. */
export const STATUS_KEYS = [
  "running",
  "queued",
  "waiting",
  "escalated",
  "failed",
  "success",
] as const satisfies readonly (keyof Palette)[]

export function findTheme(id: string | null | undefined): Theme | undefined {
  return THEMES.find((theme) => theme.id === id)
}

export function isThemeId(value: unknown): value is string {
  return typeof value === "string" && THEMES.some((theme) => theme.id === value)
}
