/* The responsive contract, asserted the only way it can be.
 *
 * jsdom computes no layout: every box is zero wide, every media query is
 * whatever the stub says, and a rendered test therefore cannot tell a shell
 * that flexes from one that clips its own controls off the side of the window.
 * What *is* checkable is the source of the behaviour — the declarations
 * themselves — so this file reads the stylesheets back off disk and asserts the
 * facts a browser would act on, the way `data-table.test.tsx` guards the
 * pinned-column invariants it also cannot see.
 *
 * These are guards, not documentation. Each one stands for a defect that was
 * found by hand-tracing layout at 320px and is invisible to every other test in
 * the suite.
 */
import { readdirSync, readFileSync, statSync } from "node:fs"
import { dirname, join, relative } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"

/* `src/`, from this file rather than from the process's working directory: a
   test that only passes when it is run from the package root is a test that
   fails in CI for a reason that has nothing to do with the product. */
const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..")

function stylesheets(root: string): string[] {
  const found: string[] = []
  for (const entry of readdirSync(root)) {
    const path = join(root, entry)
    if (statSync(path).isDirectory()) {
      // `_legacy/` is quarantined shadcn and is not held to the kit's rules.
      if (entry !== "_legacy" && entry !== "node_modules") {
        found.push(...stylesheets(path))
      }
    } else if (entry.endsWith(".css")) {
      found.push(path)
    }
  }
  return found
}

const COMMENTS = /\/\*[\s\S]*?\*\//g

function read(path: string): string {
  return readFileSync(join(SRC, path), "utf8").replace(COMMENTS, "")
}

/** The declaration bodies of every rule in `sheet` whose selector list names `selector`. */
function rules(sheet: string, selector: string): string[] {
  const found: string[] = []
  const block = /([^{}]+)\{([^{}]*)\}/g
  let match: RegExpExecArray | null
  while ((match = block.exec(sheet)) !== null) {
    const named = (match[1] ?? "")
      .split(",")
      .map((part) => part.trim())
      .includes(selector)
    if (named) {
      found.push(match[2] ?? "")
    }
  }
  return found
}

/** One declared value, or `undefined` when no rule for `selector` sets it. */
function declared(
  sheet: string,
  selector: string,
  property: string
): string | undefined {
  for (const body of rules(sheet, selector)) {
    const hit = new RegExp(`(?:^|;)\\s*${property}\\s*:([^;]+)`).exec(body)
    if (hit) {
      return (hit[1] ?? "").trim()
    }
  }
  return undefined
}

/** Every declared value for `property` under `selector`, in source order. */
function declaredAll(
  sheet: string,
  selector: string,
  property: string
): string[] {
  const found: string[] = []
  for (const body of rules(sheet, selector)) {
    const each = new RegExp(`(?:^|;)\\s*${property}\\s*:([^;]+)`, "g")
    let hit: RegExpExecArray | null
    while ((hit = each.exec(body)) !== null) {
      found.push((hit[1] ?? "").trim())
    }
  }
  return found
}

/* ------------------------------------------------------------------ *
 * The page body never scrolls sideways.
 *
 * `html` and `body` are `overflow: hidden`, so there is no page scrollbar to
 * scroll sideways *with*: anything wider than its box is simply lost. That
 * makes "does not overflow" a correctness property here rather than a cosmetic
 * one, and it is bought in three places — the header band wraps, the track it
 * sits in has no min-content floor, and the shell clips rather than hides.
 * ------------------------------------------------------------------ */

describe("the header band grows downward, never sideways", () => {
  const toolbar = read("shared/ui/data-table/data-table-toolbar.module.css")
  const header = read("app/layout/page-header.module.css")

  it("wraps the toolbar row instead of pushing the tail out of the box", () => {
    // `.tail` is `flex: 0 0 auto` — a row count and a column manager have no
    // narrower reading — so without wrapping the bar's min-content is wider
    // than the header on a narrow board, and `AppShell` clips the overflow.
    // Clipped is worse than stacked: the header is the one band that
    // deliberately does not scroll, so there is no gesture that recovers it.
    expect(declared(toolbar, ".bar", "flex-wrap")).toBe("wrap")
  })

  it("keeps the toolbar's own boxes shrinkable", () => {
    for (const selector of [".bar", ".controls", ".chips", ".tail"]) {
      expect({ selector, min: declared(toolbar, selector, "min-inline-size") })
        .toEqual({ selector, min: "0" })
    }
  })

  it("gives the filter slot a track with no min-content floor", () => {
    // An `auto` grid track is floored by its item's min-content, which would
    // size the band from the toolbar's minimum and push it past the header.
    // A zero-floored track hands the child the width there is and lets the
    // child's own wrapping decide.
    expect(declared(header, ".filters", "grid-template-columns")).toBe(
      "minmax(0, 1fr)"
    )
  })

  it("lets an unbreakable title and crumb give way rather than overflow", () => {
    // A run id has no space to break at, so without these the one long word
    // sets the header's min-content and the band leaves the window.
    expect(declared(header, ".title", "overflow-wrap")).toBe("anywhere")
    expect(declared(header, ".summary", "overflow-wrap")).toBe("anywhere")
    expect(declared(header, ".crumb", "text-overflow")).toBe("ellipsis")
    expect(declared(header, ".crumb", "max-inline-size")).toBe("100%")
  })
})

/* ------------------------------------------------------------------ *
 * The Clip-Not-Hidden Rule, above the table as well as inside it.
 * ------------------------------------------------------------------ */

describe("no ancestor of a sticky cell opens a scroll port by accident", () => {
  const shell = read("app/layout/app-shell.module.css")
  const split = read("shared/ui/split-pane/split-pane.module.css")

  it("clips the shell's content region rather than hiding it", () => {
    // `hidden` makes this box a scroll container, and `position: sticky`
    // resolves against the nearest scrolling ancestor. Today the data table's
    // own port is nearer so nothing breaks — but the first sticky element a
    // screen puts straight into the content region would anchor here instead.
    expect(declared(shell, ".main", "overflow")).toBe("clip")
  })

  it("clips a split panel rather than hiding it", () => {
    expect(declared(split, ".panel", "overflow")).toBe("clip")
  })

  it("leaves the screen's scroll on the region that owns it", () => {
    expect(declared(shell, ".content", "overflow")).toBe("auto")
    expect(declared(shell, ".content", "min-height")).toBe("0")
  })
})

/* ------------------------------------------------------------------ *
 * Overlays fit the window they open in.
 * ------------------------------------------------------------------ */

describe("dialogs and popovers are capped by the viewport", () => {
  const confirm = read("shared/ui/confirm-dialog.module.css")
  const form = read("shared/ui/form/form.module.css")
  const toolbar = read("shared/ui/data-table/data-table-toolbar.module.css")

  it("caps both dialogs on the inline axis", () => {
    // `--modal-w` is 26rem — wider than a 320px window on its own.
    expect(declared(confirm, ".modal", "max-width")).toBe(
      "calc(100vw - var(--s8))"
    )
    expect(declared(form, ".modal", "max-inline-size")).toBe(
      "calc(100vw - var(--s8))"
    )
  })

  it("caps both dialogs against the *small* viewport on the block axis", () => {
    // `dvh` last, `vh` first: `100vh` is the large viewport on a mobile
    // browser — the one with the address bar retracted — so a dialog capped at
    // `vh` alone puts its own footer under the chrome, and the footer is where
    // the submit lives. The pair is a fallback, so both must be present and
    // `dvh` must come second.
    for (const [sheet, property] of [
      [confirm, "max-height"],
      [form, "max-block-size"],
    ] as const) {
      const caps = declaredAll(sheet, ".modal", property)
      expect(caps).toEqual([
        "calc(100vh - var(--s8))",
        "calc(100dvh - var(--s8))",
      ])
    }
  })

  it("wraps a dialog footer rather than painting it over the scrim", () => {
    // Neither modal clips its own overflow, so an unwrapped footer at the
    // dialog's narrow ceiling draws buttons outside the box, over the scrim,
    // where they cannot be pressed.
    expect(declared(confirm, ".footer", "flex-wrap")).toBe("wrap")
    expect(declared(form, ".footer", "flex-wrap")).toBe("wrap")
  })

  it("lets the filter sheet stack instead of hanging off the edge", () => {
    // React Aria flips and shifts an overlay against the viewport; it does not
    // shrink one. A fixed track floor is therefore a promise the popover cannot
    // keep on a narrow board, and `auto-fit` is what turns "no room for two"
    // into one column rather than into overflow.
    const tracks = declared(toolbar, ".fields", "grid-template-columns") ?? ""
    expect(tracks).toContain("auto-fit")
    expect(tracks).toContain("min(9.5rem, 100%)")
    expect(declared(toolbar, ".sheetPopover", "max-inline-size")).toBe(
      "calc(100vw - var(--s8))"
    )
  })
})

/* ------------------------------------------------------------------ *
 * Every authored moment is opt-out.
 * ------------------------------------------------------------------ */

/** A sheet split into what sits inside a reduced-motion guard and what does not. */
function motionLayers(sheet: string) {
  const guarded: { query: string; body: string }[] = []
  let plain = sheet
  const media = /@media\s*\(\s*prefers-reduced-motion\s*:\s*([\w-]+)\s*\)\s*\{/g
  let match: RegExpExecArray | null
  while ((match = media.exec(sheet)) !== null) {
    // Walk to the matching brace: the block holds whole rules, so counting is
    // enough — CSS has no braces inside a value that would fool it here.
    let depth = 1
    let index = media.lastIndex
    while (index < sheet.length && depth > 0) {
      if (sheet[index] === "{") depth += 1
      if (sheet[index] === "}") depth -= 1
      index += 1
    }
    const body = sheet.slice(media.lastIndex, index - 1)
    guarded.push({ query: match[1] ?? "", body })
    plain = plain.replace(sheet.slice(match.index, index), "")
  }
  return { guarded, plain }
}

/** `{selector, property}` pairs that start motion outside any guard. */
function unguardedMotion(sheet: string): string[] {
  const { guarded, plain } = motionLayers(sheet)
  const cancelled = new Set<string>()
  for (const { query, body } of guarded) {
    if (query !== "reduce") {
      continue
    }
    const block = /([^{}]+)\{([^{}]*)\}/g
    let rule: RegExpExecArray | null
    while ((rule = block.exec(body)) !== null) {
      const declarations = rule[2] ?? ""
      for (const property of ["transition", "animation"]) {
        if (new RegExp(`${property}\\s*:\\s*none`).test(declarations)) {
          for (const selector of (rule[1] ?? "").split(",")) {
            cancelled.add(`${selector.trim()} ${property}`)
          }
        }
      }
    }
  }

  const offenders: string[] = []
  const block = /([^{}]+)\{([^{}]*)\}/g
  let rule: RegExpExecArray | null
  while ((rule = block.exec(plain)) !== null) {
    const selectors = (rule[1] ?? "").split(",").map((part) => part.trim())
    if (selectors.some((part) => part.startsWith("@") || part === "")) {
      continue
    }
    const declarations = rule[2] ?? ""
    const moves = [
      // `transform` is the property that actually moves a box. A colour or an
      // opacity transition is a state change, not a journey.
      /(?:^|;)\s*transition\s*:[^;]*\btransform\b/.test(declarations),
      /(?:^|;)\s*transition\s*:\s*all\b/.test(declarations),
      /(?:^|;)\s*animation\s*:\s*(?!none)/.test(declarations),
    ].some(Boolean)
    if (!moves) {
      continue
    }
    const property = /(?:^|;)\s*animation\s*:\s*(?!none)/.test(declarations)
      ? "animation"
      : "transition"
    for (const selector of selectors) {
      if (!cancelled.has(`${selector} ${property}`)) {
        offenders.push(`${selector} { ${property} }`)
      }
    }
  }
  return offenders
}

describe("motion is opt-out everywhere the kit and the shell author it", () => {
  const sheets = [...stylesheets(join(SRC, "app")), ...stylesheets(join(SRC, "shared"))]

  it("finds the stylesheets it is meant to be reading", () => {
    // A traversal that silently found nothing would make every case below pass.
    expect(sheets.length).toBeGreaterThan(8)
  })

  it.each(sheets.map((path) => [relative(SRC, path).replace(/\\/g, "/"), path]))(
    "%s starts no motion outside a prefers-reduced-motion guard",
    (_name, path) => {
      // Two spellings count as a guard, and both are in use: the declaration
      // sits inside `no-preference`, or a `reduce` block cancels it. What is
      // not allowed is a third: motion with no opt-out at all.
      expect(unguardedMotion(readFileSync(path, "utf8").replace(COMMENTS, ""))).toEqual([])
    }
  )
})

/* ------------------------------------------------------------------ *
 * Two breakpoints, and no third.
 * ------------------------------------------------------------------ */

describe("the product declares exactly the two widths DESIGN.md names", () => {
  it("uses 1240px and 1000px and nothing else", () => {
    // DESIGN.md: "Two breakpoints, both about desks rather than phones." A
    // third width is a design decision that needs an argument, not something
    // that should arrive inside a component fix. Container queries are
    // deliberately not counted — a box reacting to its own width is the
    // preferred mechanism here and is not a breakpoint at all.
    const widths = new Set<string>()
    for (const path of stylesheets(SRC)) {
      const sheet = readFileSync(path, "utf8").replace(COMMENTS, "")
      const query = /@media[^{]*?\(\s*(?:max|min)-width\s*:\s*([^)]+)\)/g
      let hit: RegExpExecArray | null
      while ((hit = query.exec(sheet)) !== null) {
        widths.add((hit[1] ?? "").trim())
      }
    }
    expect([...widths].sort()).toEqual(["1000px", "1240px"])
  })
})
