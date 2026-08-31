/* Writes src/app/styles/themes.css from the registry.
 *
 *   bun src/app/theme/gen-themes.ts
 *
 * Nothing imports this at runtime — it is a one-shot writer, kept beside the
 * registry rather than in `scripts/` so the whole theme system is one
 * directory. `theme-css.test.ts` runs the same builder and fails if the file on
 * disk disagrees, so forgetting to run this is caught by the suite rather than
 * discovered on a screen.
 */
import { writeFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"

import { buildThemesCss } from "./theme-css"
import { THEMES } from "./themes"

const here = dirname(fileURLToPath(import.meta.url))
const target = join(here, "..", "styles", "themes.css")

writeFileSync(target, buildThemesCss(THEMES), "utf8")
console.log(`wrote ${target}`)
