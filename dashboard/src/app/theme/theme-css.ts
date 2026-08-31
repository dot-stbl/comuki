import type { Palette, Theme } from "./themes"
import { DEFAULT_THEME_ID } from "./themes"

/* The registry, written out as the stylesheet the browser actually reads.
 *
 * Tokens live in CSS — a screen reads `var(--st-failed)`, never a JavaScript
 * object — but the *values* live in `themes.ts`, because contrast floors and
 * colour-blindness separations have to be computed, and you cannot compute over
 * a stylesheet. This function is the seam: it turns the registry into
 * `src/app/styles/themes.css`, and `theme-css.test.ts` asserts that the file on
 * disk is exactly what it returns. The two cannot drift; one of them is a
 * function of the other.
 *
 * To change a colour: edit `themes.ts`, then regenerate the sheet with
 *   bun src/app/theme/gen-themes.ts
 */

/** Semantic token ← palette primitive. Everything else is derived in `tokens.css`. */
export const TOKEN_MAP = [
  ["--background", "floor"],
  ["--rail", "rail"],
  ["--lane", "lane"],
  ["--lane-alt", "laneAlt"],
  ["--card", "lane"],
  ["--popover", "raised"],
  ["--accent", "hover"],
  ["--border", "rule"],
  ["--border-strong", "ruleStrong"],
  ["--foreground", "text"],
  ["--muted-foreground", "muted"],
  ["--text-faint", "faint"],
  ["--primary", "accent"],
  ["--primary-foreground", "accentFg"],
  ["--destructive", "destructive"],
  ["--st-running", "running"],
  ["--st-queued", "queued"],
  ["--st-waiting", "waiting"],
  ["--st-escalated", "escalated"],
  ["--st-failed", "failed"],
  ["--st-success", "success"],
] as const satisfies readonly (readonly [string, keyof Palette])[]

/** The custom properties a theme block sets, in emission order. */
export const THEMED_TOKENS: readonly string[] = TOKEN_MAP.map(
  ([token]) => token
)

function block(selectors: readonly string[], palette: Palette): string {
  const body = TOKEN_MAP.map(
    ([token, key]) => `  ${token}: ${palette[key]};`
  ).join("\n")
  return `${selectors.join(",\n")} {\n${body}\n}`
}

/**
 * Selectors for one theme in one mode.
 *
 * The default theme also answers to the bare `:root` / `:root.dark` pair, so a
 * document that has never had `data-theme` written to it — the first paint,
 * a test that renders a component on its own, a storage-denied browser — is
 * still fully themed rather than half-declared.
 *
 * Specificity is the reason the blocks are ordered default-first: a theme's own
 * light block (0,2,0) has to outrank `:root.dark` (0,2,0) so that switching
 * theme in dark mode does not leave half the dark defaults showing through, and
 * with equal specificity that is decided by source order. Its dark block
 * (0,3,0) then outranks its light one.
 */
function selectorsFor(id: string, mode: "light" | "dark"): readonly string[] {
  const attribute = `:root[data-theme="${id}"]`
  const suffix = mode === "dark" ? ".dark" : ""
  if (id === DEFAULT_THEME_ID) {
    return [`:root${suffix}`, `${attribute}${suffix}`]
  }
  return [`${attribute}${suffix}`]
}

const HEADER = `/* GENERATED FILE — do not edit by hand.

   Source: src/app/theme/themes.ts (the registry)
   Writer: src/app/theme/theme-css.ts (buildThemesCss)
   Regenerate: bun src/app/theme/gen-themes.ts
   Guard: src/app/theme/theme-css.test.ts fails if this file and the registry
          disagree, so an edit here is not a shortcut — it is a test failure.

   Only the primitives live here. The forty-odd semantic tokens the product
   actually reads are derived from these in tokens.css, once, so a new theme
   never has to know that \`--sidebar-ring\` exists. */
`

/* Preview swatches.
 *
 * A theme picker has to draw six palettes while wearing one of them, and a
 * component is not allowed to hold a hex — so every theme also publishes five
 * of its own colours under names that are readable from any theme. Both modes
 * are emitted, so a swatch shows the palette as it would look in the room the
 * person is actually standing in.
 *
 * These are the only tokens in the product whose value does not depend on the
 * active theme; they are labelled `--preview-*` so nothing mistakes one for a
 * surface it could paint with. */
// The card draws five bands plus a mark that has to survive whatever band it
// lands on, so the preview set is the chrome a theme builds from (floor, rail),
// the three colours that carry meaning, the rule that bounds the card, and the
// text colour the mark is drawn in.
const PREVIEW_KEYS = [
  "floor",
  "rail",
  "rule",
  "text",
  "accent",
  "running",
  "failed",
] as const satisfies readonly (keyof Palette)[]

function previewBlock(
  selector: string,
  themes: readonly Theme[],
  mode: "light" | "dark"
): string {
  const body = themes
    .flatMap((theme) =>
      PREVIEW_KEYS.map(
        (key) =>
          `  --preview-${theme.id}-${key.toLowerCase()}: ${theme.palette[mode][key]};`
      )
    )
    .join("\n")
  return `${selector} {\n${body}\n}`
}

/** The custom property a swatch reads for one theme's one colour. */
export function previewToken(
  themeId: string,
  key: (typeof PREVIEW_KEYS)[number]
): string {
  return `--preview-${themeId}-${key}`
}

export const PREVIEW_TOKEN_KEYS: readonly string[] = PREVIEW_KEYS

/** The whole of `src/app/styles/themes.css`, from the registry. */
export function buildThemesCss(themes: readonly Theme[]): string {
  const ordered = [
    ...themes.filter((theme) => theme.id === DEFAULT_THEME_ID),
    ...themes.filter((theme) => theme.id !== DEFAULT_THEME_ID),
  ]
  const blocks: string[] = []
  for (const theme of ordered) {
    blocks.push(`/* ${theme.name} — ${theme.note} */`)
    blocks.push(block(selectorsFor(theme.id, "light"), theme.palette.light))
    blocks.push(block(selectorsFor(theme.id, "dark"), theme.palette.dark))
  }
  blocks.push(
    "/* Preview swatches — every theme's own colours, readable from any theme. */"
  )
  blocks.push(previewBlock(":root", ordered, "light"))
  blocks.push(previewBlock(":root.dark", ordered, "dark"))
  return `${HEADER}\n${blocks.join("\n\n")}\n`
}
