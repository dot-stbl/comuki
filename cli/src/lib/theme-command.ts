/**
 * `/theme` listing and switch notices. The registry and install live
 * in `themes.ts` / `theme.ts`; this module is the transcript chrome
 * the slash command prints — seven palettes, current mode marked.
 */
import { CLI_THEMES } from "../themes"
import { colors, symbols } from "../theme"

/** One row per palette: `id` then `dark` / `light`, `*` on the active choice. */
export function themeListingLines(currentId: string): readonly string[] {
  const width = Math.max(...CLI_THEMES.map((theme) => theme.id.length))
  return [
    `${colors.accent}themes${colors.reset}`,
    ...CLI_THEMES.map((theme) => {
      const dark = `${theme.id}-dark`
      const light = `${theme.id}-light`
      const darkMark = currentId === dark ? "*" : " "
      const lightMark = currentId === light ? "*" : " "
      return `  ${theme.id.padEnd(width + 3)}dark ${darkMark}  light ${lightMark}`
    }),
  ]
}

/** Dim notice after a live switch — `⏺ theme → dockside-dark`. */
export function themeSwitchedLine(id: string): string {
  return `${colors.faint}  ${symbols.event} theme → ${id}${colors.reset}`
}

/**
 * Invalid name: error line plus the listing so the user can pick a
 * real `<id>-<dark|light>` without leaving the transcript.
 */
export function themeUnknownLines(
  name: string,
  currentId: string
): readonly string[] {
  return [
    `${colors.error}${symbols.cross} unknown theme: ${name}${colors.reset}`,
    ...themeListingLines(currentId),
  ]
}

