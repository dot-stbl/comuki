/* The registry and the stylesheets cannot drift apart.
 *
 * There are two files that could disagree and one that must not exist:
 *
 *   themes.ts   the values, in a form arithmetic can be done over
 *   themes.css  the same values, in the form a browser reads
 *   tokens.css  the vocabulary — every token derived from those values
 *
 * The first two are held together by regenerating one from the other and
 * comparing, so "I edited the CSS by hand" is a test failure with a diff rather
 * than a colour that is right in the tests and wrong on the screen. The third is
 * held to the second by checking that every primitive `tokens.css` reaches for
 * is one that *every* theme states — a theme that forgot `--lane-alt` would
 * otherwise render six screens with a transparent row band and no error
 * anywhere.
 */
import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"

import { buildThemesCss, THEMED_TOKENS } from "./theme-css"
import { DEFAULT_THEME_ID, THEMES } from "./themes"

const HERE = dirname(fileURLToPath(import.meta.url))
const STYLES = join(HERE, "..", "styles")

const COMMENTS = /\/\*[\s\S]*?\*\//g

function read(file: string): string {
  return readFileSync(join(STYLES, file), "utf8")
}

/** `[selector list, declaration body]` for every rule in a sheet. */
function ruleBlocks(sheet: string): { selectors: string; body: string }[] {
  const found: { selectors: string; body: string }[] = []
  const block = /([^{}]+)\{([^{}]*)\}/g
  let match: RegExpExecArray | null
  while ((match = block.exec(sheet.replace(COMMENTS, ""))) !== null) {
    found.push({ selectors: (match[1] ?? "").trim(), body: match[2] ?? "" })
  }
  return found
}

/** The custom properties a declaration body sets. */
function declaredProperties(body: string): string[] {
  return [...body.matchAll(/(?:^|;)\s*(--[a-z0-9-]+)\s*:/g)].map(
    (hit) => hit[1] as string
  )
}

/** The custom properties a stylesheet reads through `var()`. */
function referencedProperties(sheet: string): string[] {
  return [
    ...sheet.replace(COMMENTS, "").matchAll(/var\(\s*(--[a-z0-9-]+)/g),
  ].map((hit) => hit[1] as string)
}

describe("themes.css is the registry, compiled", () => {
  it("matches what the builder produces, byte for byte", () => {
    // If this fails: `bun src/app/theme/gen-themes.ts`.
    expect(read("themes.css")).toBe(buildThemesCss(THEMES))
  })

  it("gives every theme a light block and a dark block", () => {
    const selectors = ruleBlocks(read("themes.css")).map(
      (rule) => rule.selectors
    )
    for (const theme of THEMES) {
      expect(selectors).toContain(
        theme.id === DEFAULT_THEME_ID
          ? `:root,\n:root[data-theme="${theme.id}"]`
          : `:root[data-theme="${theme.id}"]`
      )
      expect(selectors).toContain(
        theme.id === DEFAULT_THEME_ID
          ? `:root.dark,\n:root[data-theme="${theme.id}"].dark`
          : `:root[data-theme="${theme.id}"].dark`
      )
    }
  })

  it("lets the default answer to a document that has no data-theme yet", () => {
    // First paint, a component rendered on its own in a test, a browser that
    // denied storage: none of those have had the attribute written, and all
    // three still have to be fully themed rather than half-declared.
    const selectors = ruleBlocks(read("themes.css")).map(
      (rule) => rule.selectors
    )
    const bare = selectors.filter(
      (selector) => selector === ":root" || selector.startsWith(":root,")
    )
    expect(bare.length).toBeGreaterThan(0)
  })

  it("has every theme state exactly the same properties", () => {
    // Not "at least" — exactly. A theme with an extra token would be one that
    // only works while it is the one selected.
    const expected = [...THEMED_TOKENS].sort()
    for (const rule of ruleBlocks(read("themes.css"))) {
      if (!rule.selectors.includes("data-theme")) {
        continue
      }
      if (rule.selectors.includes("--preview")) {
        continue
      }
      const properties = declaredProperties(rule.body)
      if (properties.some((property) => property.startsWith("--preview-"))) {
        continue
      }
      expect({
        selectors: rule.selectors,
        properties: [...properties].sort(),
      }).toEqual({ selectors: rule.selectors, properties: expected })
    }
  })

  it("publishes a preview swatch for every theme, in both modes", () => {
    const sheet = read("themes.css")
    for (const theme of THEMES) {
      for (const key of ["floor", "rule", "accent", "running", "failed"]) {
        const token = `--preview-${theme.id}-${key}`
        // Once under `:root` and once under `:root.dark` — a swatch follows the
        // mode, so a theme previewed in a dark room shows its dark rendering.
        expect({
          token,
          count: sheet.split(`${token}:`).length - 1,
        }).toEqual({ token, count: 2 })
      }
    }
  })
})

describe("tokens.css owns the vocabulary and nothing else", () => {
  it("declares no property a theme also declares", () => {
    // Two files setting one property is the one way this split can go wrong:
    // whichever loads last silently wins, and the loser is invisible.
    const themed = new Set(THEMED_TOKENS)
    const clashes = ruleBlocks(read("tokens.css"))
      .flatMap((rule) => declaredProperties(rule.body))
      .filter((property) => themed.has(property))
    expect([...new Set(clashes)]).toEqual([])
  })

  it("reaches for no primitive that some theme fails to state", () => {
    // This is the drift the split exists to make impossible: a token the
    // semantic layer derives from has to be declared by every theme, or the
    // derivation resolves to nothing on the theme that forgot it.
    const tokens = read("tokens.css")
    const local = new Set(
      ruleBlocks(tokens).flatMap((rule) => declaredProperties(rule.body))
    )
    const themed = new Set(THEMED_TOKENS)
    const orphans = [...new Set(referencedProperties(tokens))].filter(
      (property) => !local.has(property) && !themed.has(property)
    )
    expect(orphans).toEqual([])
  })

  it("keeps the dark room to depth alone", () => {
    // Every palette value comes from a theme block in both modes. If `.dark`
    // in tokens.css ever set a colour again, that colour would outrank five of
    // the six themes.
    const dark = ruleBlocks(read("tokens.css")).filter((rule) =>
      rule.selectors.includes(".dark")
    )
    expect(dark.length).toBe(1)
    expect(declaredProperties(dark[0]?.body ?? "").sort()).toEqual([
      "--scrim",
      "--shadow-header",
      "--shadow-lift",
      "--shadow-modal",
      "--shadow-pinned",
    ])
  })

  it("still declares the weave for all six statuses", () => {
    // The second channel. It is what lets four of the six statuses survive
    // greyscale on a hatch rather than on a hue, and it is deliberately
    // theme-independent — a palette changes the colours, never the encoding.
    const tokens = read("tokens.css")
    const declared = new Set(
      ruleBlocks(tokens).flatMap((rule) => declaredProperties(rule.body))
    )
    for (const status of [
      "running",
      "success",
      "waiting",
      "escalated",
      "failed",
      "queued",
    ]) {
      expect({ status, declared: declared.has(`--weave-${status}`) }).toEqual({
        status,
        declared: true,
      })
    }
  })

  it("keeps the four hatches distinct from one another", () => {
    // `running` and `success` are `none` on purpose — they are separated by
    // colour, which is why `palette.test.ts` holds them furthest apart. The
    // other four have to differ, or two statuses share an encoding.
    const body =
      ruleBlocks(read("tokens.css")).find((rule) => rule.selectors === ":root")
        ?.body ?? ""
    const hatches = ["waiting", "escalated", "failed", "queued"].map(
      (status) =>
        new RegExp(`--weave-${status}\\s*:([\\s\\S]*?);`)
          .exec(body)?.[1]
          ?.replace(/\s+/g, " ")
          .trim() ?? ""
    )
    expect(hatches.filter((hatch) => hatch.length > 0)).toHaveLength(4)
    expect(new Set(hatches).size).toBe(4)
  })
})

describe("the sheets are loaded in the order the cascade needs", () => {
  it("imports the values before the vocabulary", () => {
    const index = readFileSync(join(STYLES, "..", "..", "index.css"), "utf8")
    const themes = index.indexOf('@import "./app/styles/themes.css"')
    const tokens = index.indexOf('@import "./app/styles/tokens.css"')
    expect(themes).toBeGreaterThan(-1)
    expect(tokens).toBeGreaterThan(themes)
  })
})
