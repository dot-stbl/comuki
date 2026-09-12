/* The two channels a status is made of, resolved the way a browser resolves
 * them.
 *
 * `tokens.css` now carries the product's only `--hue` / `--weave` declaration.
 * Six modules used to each carry their own — two near-identical six-arm
 * copies, one with a `clear` alias bolted on, and three that skipped `--hue`
 * and reached for `var(--st-…)` directly — and the whole point of collapsing
 * them is that **nothing on any screen may move**. A refactor of a visual
 * surface that changes one pixel of one status has failed, however much
 * cleaner the sheets read afterwards.
 *
 * jsdom lays nothing out, paints nothing, and its `getComputedStyle` does not
 * substitute `var()`, so no rendered test can see the thing that must not have
 * changed. What *is* checkable is the cascade itself, arithmetically: read the
 * sheets back off disk the way `theme-css.test.ts` and `badge-fit.test.ts`
 * already do, run the real selectors against real DOM shapes with
 * `Element.matches`, resolve the winning declarations through their `var()`
 * chains, and compare the strings a browser would end up with against the
 * registry and against the hatch angles written here as literals.
 *
 * So this file asserts three separate things:
 *
 *   one declaration    no stylesheet but `tokens.css` sets either property,
 *                      which is also what makes the cascade order below
 *                      irrelevant — there is nothing left to order.
 *   the right pair     every status resolves to its own theme colour and its
 *                      own hatch, in both modes, anchored to `themes.ts` and
 *                      to angles spelled out here rather than read back out
 *                      of the file under test.
 *   the same painting  every consumer ends up with the exact declarations it
 *                      ended up with before — including the two that take the
 *                      hue and deliberately refuse the weave, and the log,
 *                      which colours four of the seven and not every one.
 */
import { readdirSync, readFileSync } from "node:fs"
import { dirname, join, relative, sep } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"

import { DEFAULT_THEME_ID, STATUS_KEYS, THEMES } from "../theme/themes"

const HERE = dirname(fileURLToPath(import.meta.url))
/** `src/`, from this file rather than from the process's working directory. */
const SRC = join(HERE, "..", "..")

const COMMENTS = /\/\*[\s\S]*?\*\//g

function read(fromSrc: string): string {
  return readFileSync(join(SRC, ...fromSrc.split("/")), "utf8")
}

/* ------------------------------------------------------------------ *
 * A cascade small enough to read, large enough to be the real one.
 * ------------------------------------------------------------------ */

interface Rule {
  selectors: string
  body: string
}

/** `[selector list, declaration body]` for every flat rule in a sheet. */
function ruleBlocks(sheet: string): Rule[] {
  const found: Rule[] = []
  const block = /([^{}]+)\{([^{}]*)\}/g
  let match: RegExpExecArray | null
  while ((match = block.exec(sheet.replace(COMMENTS, ""))) !== null) {
    found.push({ selectors: (match[1] ?? "").trim(), body: match[2] ?? "" })
  }
  return found
}

/**
 * `[property, value]` for a declaration body.
 *
 * Split at top-level semicolons and colons only: `color-mix(in oklab, x 7%,
 * transparent)` and `repeating-linear-gradient(0deg, …)` both carry commas and
 * one of them carries a colon-free `in oklab`, and a naive split turns either
 * into two broken declarations.
 */
function declarationsOf(body: string): [string, string][] {
  const out: [string, string][] = []
  let depth = 0
  let buffer = ""
  const flush = (): void => {
    const text = buffer.trim()
    buffer = ""
    if (!text) {
      return
    }
    let inner = 0
    for (let i = 0; i < text.length; i += 1) {
      const ch = text[i]
      if (ch === "(") inner += 1
      else if (ch === ")") inner -= 1
      else if (ch === ":" && inner === 0) {
        out.push([text.slice(0, i).trim(), text.slice(i + 1).trim()])
        return
      }
    }
  }
  for (const ch of body) {
    if (ch === "(") depth += 1
    else if (ch === ")") depth -= 1
    if (ch === ";" && depth === 0) {
      flush()
    } else {
      buffer += ch
    }
  }
  flush()
  return out
}

/**
 * Selector weight, as one comparable number.
 *
 * Ids, then classes/attributes/pseudo-classes, then element names — enough for
 * the selectors in play here, all of which are flat class-and-attribute
 * compounds.
 */
function specificity(selector: string): number {
  const ids = selector.match(/#[\w-]+/g)?.length ?? 0
  const classes =
    selector.match(/\.[\w-]+|\[[^\]]*\]|:{1,2}[a-z-]+(\([^)]*\))?/g)?.length ??
    0
  const types = selector.match(/(^|[\s>+~])[a-z][\w-]*/g)?.length ?? 0
  return ids * 1_000_000 + classes * 1_000 + types
}

interface Sheet {
  name: string
  text: string
}

/**
 * `node.matches(selector)`, with an unparseable selector treated as no match.
 *
 * The flat rule regex above cuts an at-rule prelude in half — `@media (…) {
 * .node` arrives here as a selector no engine will accept. Nothing in these
 * sheets overrides a status channel inside a media query, and "no sheet but
 * `tokens.css` declares one" is asserted separately over the raw text, where
 * an at-rule cannot hide it.
 */
function matchesSafely(node: Element, selector: string): boolean {
  try {
    return node.matches(selector)
  } catch {
    return false
  }
}

/** Every declaration that wins on `node`, given these sheets in load order. */
function winners(node: Element, sheets: readonly Sheet[]): Map<string, string> {
  const best = new Map<string, { rank: number; value: string }>()
  let order = 0
  for (const sheet of sheets) {
    for (const rule of ruleBlocks(sheet.text)) {
      order += 1
      for (const selector of rule.selectors.split(",").map((s) => s.trim())) {
        if (!matchesSafely(node, selector)) {
          continue
        }
        const rank = specificity(selector) * 100_000 + order
        for (const [property, value] of declarationsOf(rule.body)) {
          const prior = best.get(property)
          if (!prior || prior.rank <= rank) {
            best.set(property, { rank, value })
          }
        }
      }
    }
  }
  return new Map([...best].map(([property, hit]) => [property, hit.value]))
}

/** A value that resolved through an unset `var()` — invalid at computed-value time. */
const UNSET = "<unset>"

/** Substitute `var()` the way the browser does, fallbacks included. */
function expand(
  value: string,
  props: ReadonlyMap<string, string>,
  depth = 0
): string {
  if (depth > 24) {
    throw new Error(`var() cycle while resolving: ${value}`)
  }
  let out = ""
  let i = 0
  while (i < value.length) {
    if (!value.startsWith("var(", i)) {
      out += value[i]
      i += 1
      continue
    }
    let inner = 0
    let j = i + 3
    for (; j < value.length; j += 1) {
      if (value[j] === "(") inner += 1
      else if (value[j] === ")") {
        inner -= 1
        if (inner === 0) break
      }
    }
    const args = value.slice(i + 4, j)
    const comma = topLevelComma(args)
    const name = (comma < 0 ? args : args.slice(0, comma)).trim()
    const fallback = comma < 0 ? undefined : args.slice(comma + 1).trim()
    const stated = props.get(name)
    if (stated !== undefined) {
      out += expand(stated, props, depth + 1)
    } else if (fallback !== undefined) {
      out += expand(fallback, props, depth + 1)
    } else {
      out += UNSET
    }
    i = j + 1
  }
  return out
}

function topLevelComma(text: string): number {
  let depth = 0
  for (let i = 0; i < text.length; i += 1) {
    const ch = text[i]
    if (ch === "(") depth += 1
    else if (ch === ")") depth -= 1
    else if (ch === "," && depth === 0) return i
  }
  return -1
}

function flat(value: string): string {
  return value.replace(/\s+/g, " ").trim()
}

/* ------------------------------------------------------------------ *
 * The two sheets every screen loads, and the root each mode resolves on.
 * ------------------------------------------------------------------ */

const THEMES_CSS: Sheet = {
  name: "themes.css",
  text: read("app/styles/themes.css"),
}
const TOKENS_CSS: Sheet = {
  name: "tokens.css",
  text: read("app/styles/tokens.css"),
}
/** `index.css` imports the values before the vocabulary; so does this. */
const GLOBAL: readonly Sheet[] = [THEMES_CSS, TOKENS_CSS]

function rootProps(mode: "light" | "dark"): Map<string, string> {
  const props = new Map<string, string>()
  const wanted = mode === "dark" ? [":root", ":root.dark"] : [":root"]
  for (const key of wanted) {
    for (const sheet of GLOBAL) {
      for (const rule of ruleBlocks(sheet.text)) {
        const named = rule.selectors
          .split(",")
          .map((part) => part.trim())
          .includes(key)
        if (!named) continue
        for (const [property, value] of declarationsOf(rule.body)) {
          if (property.startsWith("--")) props.set(property, value)
        }
      }
    }
  }
  return props
}

const ROOT = { light: rootProps("light"), dark: rootProps("dark") } as const
const MODES = ["light", "dark"] as const

const DEFAULT_THEME = THEMES.find((theme) => theme.id === DEFAULT_THEME_ID)

/** The custom properties in force on `node`, inheritance included. */
function propsOn(
  node: Element,
  sheets: readonly Sheet[],
  mode: "light" | "dark"
): Map<string, string> {
  const chain: Element[] = []
  for (let walk: Element | null = node; walk; walk = walk.parentElement) {
    chain.unshift(walk)
  }
  const props = new Map(ROOT[mode])
  for (const element of chain) {
    for (const [property, value] of winners(element, sheets)) {
      if (property.startsWith("--")) props.set(property, value)
    }
  }
  return props
}

/** What a browser would paint for `property` on `node`, fully resolved. */
function painted(
  node: Element,
  sheets: readonly Sheet[],
  property: string,
  mode: "light" | "dark"
): string | undefined {
  const declared = winners(node, sheets).get(property)
  if (declared === undefined) {
    return undefined
  }
  return flat(expand(declared, propsOn(node, sheets, mode)))
}

/* ------------------------------------------------------------------ *
 * The DOM shapes the components actually render.
 * ------------------------------------------------------------------ */

const SVG_NS = "http://www.w3.org/2000/svg"

function node(
  tag: string,
  className: string,
  options: { status?: string; ns?: string; parent?: Element } = {}
): Element {
  const created = options.ns
    ? document.createElementNS(options.ns, tag)
    : document.createElement(tag)
  created.setAttribute("class", className)
  if (options.status !== undefined) {
    created.setAttribute("data-status", options.status)
  }
  options.parent?.append(created)
  return created
}

function sheetsFor(fromSrc: string): readonly Sheet[] {
  return [...GLOBAL, { name: fromSrc, text: read(fromSrc) }]
}

const RIVER = "domains/runs/ui/profile-river.module.css"
const GRAPH = "domains/runs/ui/run-graph.module.css"
const VERDICT = "domains/home/ui/attention-verdict.module.css"
const OUTCOMES = "domains/home/ui/outcomes-band.module.css"
const INSPECTOR = "domains/runs/ui/work-item-inspector.module.css"
const SERIES = "shared/ui/bar-series.module.css"
const BADGE = "shared/ui/status-badge.module.css"

/* ------------------------------------------------------------------ *
 * One declaration.
 * ------------------------------------------------------------------ */

function everyStylesheet(): string[] {
  const found: string[] = []
  const walk = (dir: string): void => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name)
      if (entry.isDirectory()) walk(path)
      else if (entry.name.endsWith(".css")) found.push(path)
    }
  }
  walk(SRC)
  return found
}

describe("the status pair is declared once", () => {
  it("is declared in tokens.css for all seven statuses", () => {
    // Both channels, every status. A status with a hue and no hatch is the
    // shape of the defect this whole file exists to keep out.
    const declared = new Set(
      ruleBlocks(TOKENS_CSS.text).flatMap((rule) =>
        rule.selectors.startsWith("[data-status=")
          ? declarationsOf(rule.body).map(
              ([property]) => `${rule.selectors} ${property}`
            )
          : []
      )
    )
    for (const status of STATUS_KEYS) {
      const arm = `[data-status="${status}"]`
      expect({
        status,
        hue: declared.has(`${arm} --hue`),
        weave: declared.has(`${arm} --weave`),
      }).toEqual({ status, hue: true, weave: true })
    }
  })

  it("is declared nowhere else in the product", () => {
    // The claim in DESIGN.md, as an assertion. It is also what makes the
    // cascade order between a global sheet and a CSS Module stop mattering:
    // with one source there is no second declaration to lose a race to.
    const offenders = everyStylesheet()
      .filter((path) => !path.endsWith(join("app", "styles", "tokens.css")))
      .filter((path) =>
        /(^|[;{\s])--(hue|weave)\s*:/.test(
          readFileSync(path, "utf8").replace(COMMENTS, "")
        )
      )
      .map((path) => relative(SRC, path).split(sep).join("/"))
    expect(offenders).toEqual([])
  })

  it("keeps the one non-status value the attribute carries", () => {
    // `clear` is the duty screen's verdict word for an empty list, and it is
    // still not a status — `cancelled` is the seventh, and it is one. `clear`
    // borrows success's pair and lives in tokens.css with the seven so the
    // attribute has exactly one vocabulary.
    const band = node("div", "band", { status: "clear" })
    const rail = node("span", "rail", { parent: band })
    for (const mode of MODES) {
      const success = DEFAULT_THEME?.palette[mode].success
      expect({
        mode,
        hue: painted(rail, sheetsFor(VERDICT), "background-color", mode),
      }).toEqual({ mode, hue: success })
    }
  })
})

/* ------------------------------------------------------------------ *
 * The right pair, anchored outside the file under test.
 * ------------------------------------------------------------------ */

/**
 * The hatch each status wears, as an angle rather than as a re-read of the
 * token. `running` and `success` have none on purpose — they are the pair the
 * palette holds 26 L* apart, the widest berth on the ladder, which
 * `palette.test.ts` enforces and which is only safe while this table stays
 * `null` for exactly those two. Every angle here is at least 22.5deg from
 * every other, so no two hatches read as one on a photocopy.
 */
const WEAVE_ANGLE: Record<(typeof STATUS_KEYS)[number], string | null> = {
  running: null,
  success: null,
  waiting: "0deg",
  escalated: "45deg",
  failed: "-45deg",
  queued: "90deg",
  cancelled: "67.5deg",
}

/** `--weave-<status>`, resolved. The string every consumer has to end up with. */
function weaveOf(status: string, mode: "light" | "dark"): string {
  return flat(expand(`var(--weave-${status})`, ROOT[mode]))
}

/** `--st-<status>`, resolved. */
function hueOf(status: string, mode: "light" | "dark"): string {
  return flat(expand(`var(--st-${status})`, ROOT[mode]))
}

describe("a status resolves to its own colour and its own hatch", () => {
  it("names a default theme to resolve against", () => {
    expect(DEFAULT_THEME).toBeDefined()
  })

  it("gives every status the registry's colour, in both modes", () => {
    // The anchor is `themes.ts`, not the stylesheet: a sheet compared against
    // itself would pass while every colour on the screen was wrong together.
    for (const mode of MODES) {
      for (const status of STATUS_KEYS) {
        expect({ mode, status, hue: hueOf(status, mode) }).toEqual({
          mode,
          status,
          hue: DEFAULT_THEME?.palette[mode][status],
        })
      }
    }
  })

  it("gives every status the hatch angle it is supposed to wear", () => {
    for (const status of STATUS_KEYS) {
      const angle = WEAVE_ANGLE[status]
      const weave = weaveOf(status, "light")
      expect({ status, weave }).toEqual({
        status,
        weave:
          angle === null
            ? "none"
            : expect.stringContaining(`repeating-linear-gradient( ${angle},`),
      })
    }
  })

  it("keeps every hatch angle at least 22.5deg from every other", () => {
    // Density alone is not enough to tell two hatches apart at a glance, and a
    // glance is the whole job of the second channel. Angles wrap at 180deg — a
    // line drawn at -45 and a line drawn at 135 are the same line — so the
    // distance is measured there. 22.5 is what the seventh status had left:
    // 0, 45, 90 and -45 were taken, and the widest gap between them halves to
    // exactly this. An eighth status has no room at this floor, which is the
    // useful thing for this test to say out loud.
    const angles = STATUS_KEYS.map((status) => WEAVE_ANGLE[status])
      .filter((angle): angle is string => angle !== null)
      .map((angle) => ((Number.parseFloat(angle) % 180) + 180) % 180)
    const tight: string[] = []
    for (let i = 0; i < angles.length; i += 1) {
      for (let j = i + 1; j < angles.length; j += 1) {
        const raw = Math.abs((angles[i] ?? 0) - (angles[j] ?? 0))
        const apart = Math.min(raw, 180 - raw)
        if (apart < 22.5) {
          tight.push(`${angles[i]}deg/${angles[j]}deg is ${apart}deg apart`)
        }
      }
    }
    expect(tight).toEqual([])
  })

  it("paints `cancelled` as neither `failed` nor `success`", () => {
    // The regression the seventh status exists to end. `normalizeRunStatus`
    // used to fold the wire's `cancelled` onto `failed` for want of a rung, so
    // a project that stops runs routinely painted its board red for work
    // nobody failed at — and `failed` is the one badge that escalates. Both
    // channels have to differ from both terminal neighbours, or the seventh
    // status is only a seventh word.
    for (const mode of MODES) {
      expect({
        mode,
        vsFailedHue: hueOf("cancelled", mode) === hueOf("failed", mode),
        vsFailedWeave: weaveOf("cancelled", mode) === weaveOf("failed", mode),
        vsSuccessHue: hueOf("cancelled", mode) === hueOf("success", mode),
        vsSuccessWeave: weaveOf("cancelled", mode) === weaveOf("success", mode),
      }).toEqual({
        mode,
        vsFailedHue: false,
        vsFailedWeave: false,
        vsSuccessHue: false,
        vsSuccessWeave: false,
      })
    }
  })

  it("keeps the weave the same in both modes", () => {
    // The hatch is the channel that does not depend on the palette. If a mode
    // could change it, the encoding would stop being the thing that survives.
    for (const status of STATUS_KEYS) {
      expect({
        status,
        same: weaveOf(status, "light") === weaveOf(status, "dark"),
      }).toEqual({ status, same: true })
    }
  })
})

/* ------------------------------------------------------------------ *
 * Every consumer paints exactly what it painted before.
 * ------------------------------------------------------------------ */

describe("the wordless marks take both channels", () => {
  /** The four marks that carry a status with no text of their own beside it. */
  const MARKS: readonly (readonly [string, string, () => Element])[] = [
    ["river band segment", RIVER, () => node("span", "seg")],
    ["river legend swatch", RIVER, () => node("span", "keySwatch")],
    ["run graph node edge", GRAPH, () => node("span", "edge")],
  ]

  for (const [what, sheet, build] of MARKS) {
    it(`paints ${what} with the status hue and the status weave`, () => {
      for (const mode of MODES) {
        for (const status of STATUS_KEYS) {
          const mark = build()
          mark.setAttribute("data-status", status)
          const sheets = sheetsFor(sheet)
          expect({
            what,
            mode,
            status,
            color: painted(mark, sheets, "background-color", mode),
            image: painted(mark, sheets, "background-image", mode),
          }).toEqual({
            what,
            mode,
            status,
            color: hueOf(status, mode),
            image: weaveOf(status, mode),
          })
        }
      }
    })
  }

  it("paints the verdict band's rail, wash, figure and mark from one status", () => {
    // Four surfaces off one attribute: the 2px rail is the only one with room
    // for the hatch, the band takes a 7% wash of the same hue, and the figure
    // and the check glyph take it as text.
    const sheets = sheetsFor(VERDICT)
    for (const mode of MODES) {
      for (const status of ["escalated", "failed", "waiting"] as const) {
        const band = node("div", "band", { status })
        const rail = node("span", "rail", { parent: band })
        const body = node("div", "body", { parent: band })
        const head = node("p", "head", { parent: body })
        const count = node("span", "count", { parent: head })
        const hue = hueOf(status, mode)
        expect({
          mode,
          status,
          rail: painted(rail, sheets, "background-color", mode),
          hatch: painted(rail, sheets, "background-image", mode),
          wash: painted(band, sheets, "background", mode),
          figure: painted(count, sheets, "color", mode),
        }).toEqual({
          mode,
          status,
          rail: hue,
          hatch: weaveOf(status, mode),
          wash: `color-mix(in oklab, ${hue} 7%, transparent)`,
          figure: hue,
        })
      }
    }
  })
})

describe("the marks that spell the status out take the hue alone", () => {
  it("fills a bar-series segment with the hue and no hatch", () => {
    // An SVG rect has no background to hatch, and the figure and legend beside
    // the chart carry the words. Hue here is reinforcement, not the reading.
    const sheets = sheetsFor(SERIES)
    for (const mode of MODES) {
      for (const status of STATUS_KEYS) {
        const seg = node("rect", "seg", { status, ns: SVG_NS })
        expect({
          mode,
          status,
          fill: painted(seg, sheets, "fill", mode),
          image: painted(seg, sheets, "background-image", mode),
        }).toEqual({
          mode,
          status,
          fill: hueOf(status, mode),
          image: undefined,
        })
      }
    }
  })

  it("fills the outcomes legend swatch with the hue and no hatch", () => {
    const sheets = sheetsFor(OUTCOMES)
    for (const mode of MODES) {
      for (const status of ["success", "failed", "escalated"] as const) {
        const swatch = node("span", "swatch status", { status })
        expect({
          mode,
          status,
          color: painted(swatch, sheets, "background-color", mode),
          image: painted(swatch, sheets, "background-image", mode),
        }).toEqual({
          mode,
          status,
          color: hueOf(status, mode),
          image: undefined,
        })
      }
    }
  })

  it("gives the badge the hue as text and a 10% wash, and no hatch at all", () => {
    // The deliberate hole in the set, and the reason it is one: a badge writes
    // the status out in the product's own word next to a distinct glyph, so it
    // already has two channels that survive greyscale. A hatch under 11px mono
    // would cost legibility to restate the label. Asserted rather than left to
    // the next reader to mistake for the oversight it looks like.
    const sheets = sheetsFor(BADGE)
    for (const mode of MODES) {
      for (const status of STATUS_KEYS) {
        const badge = node("span", "badge", { status })
        const hue = hueOf(status, mode)
        expect({
          mode,
          status,
          text: painted(badge, sheets, "color", mode),
          image: painted(badge, sheets, "background-image", mode),
        }).toEqual({ mode, status, text: hue, image: undefined })
      }
    }
  })

  it("keeps `failed` the one badge that escalates", () => {
    const sheets = sheetsFor(BADGE)
    for (const mode of MODES) {
      for (const status of STATUS_KEYS) {
        const badge = node("span", "badge", { status })
        const hue = hueOf(status, mode)
        const loud = status === "failed"
        expect({
          mode,
          status,
          border: painted(badge, sheets, "border-color", mode),
          wash: painted(badge, sheets, "background", mode),
        }).toEqual({
          mode,
          status,
          border: loud
            ? `color-mix(in oklab, ${hue} 40%, transparent)`
            : undefined,
          // The tint token is an 18% mix — a louder wash than the 10% every
          // other badge takes, which is the whole point of the one arm left.
          wash: loud
            ? `color-mix(in oklab, ${hue} 18%, transparent)`
            : `color-mix(in oklab, ${hue} 10%, transparent)`,
        })
      }
    }
  })
})

describe("the log still colours four statuses and not seven", () => {
  it("tints the four that mean something happened and leaves the rest", () => {
    // The subset is this module's own decision and survived the move: a log is
    // read downward hunting for the moment it went wrong, and colouring the
    // ordinary lines is what stops the extraordinary one from showing.
    // `cancelled` joined the set the module leaves alone: an operator who
    // stopped a run does not need the log to point at the line saying so.
    const sheets = sheetsFor(INSPECTOR)
    const COLOURED = new Set(["running", "failed", "waiting", "escalated"])
    for (const mode of MODES) {
      for (const status of STATUS_KEYS) {
        const event = node("li", "event", { status })
        const text = node("span", "eventText", { parent: event })
        expect({
          mode,
          status,
          color: painted(text, sheets, "color", mode),
        }).toEqual({
          mode,
          status,
          color: COLOURED.has(status)
            ? hueOf(status, mode)
            : flat(expand("var(--foreground)", ROOT[mode])),
        })
      }
    }
  })
})
