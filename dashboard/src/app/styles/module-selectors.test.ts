/* One class, one top-level block, per CSS Module.
 *
 * `domains/chat/ui/chat-message.module.css` once declared `.steps` twice at the
 * top level, in two different roles: once as the plan's step list and once as
 * something else entirely. Twelve components share that module, so the second
 * block's properties landed on the first block's list — a defect with no
 * error, no warning and no failing assertion anywhere. It passed typecheck (CSS
 * Modules are `Record<string, string>` to TypeScript), it passed lint (no CSS
 * linter runs), it passed every rendered test (jsdom computes no layout and
 * asserts on class *names*, which were identical), and it passed build (the
 * bundler's job is to concatenate, and it did). The gate is blind to it by
 * construction, which is what this file is for.
 *
 * The hazard is not "the same class appears twice" — it is **two bare
 * top-level blocks for the same class**. That shape has no reader: whoever
 * edits one cannot see the other, and which declaration wins is decided by file
 * order rather than by anybody's intent. Every other repetition of a class name
 * in a stylesheet is a reader's tool and stays legal here:
 *
 *   - a **group** — `.columns, .row { … }` then `.row { … }` — is a layer on
 *     top of a shared base, and reads as one. `home/ui/attention-list.module.css`
 *     is the reference for it;
 *   - a **state** — `.item:hover`, `.item[data-active]`, `.item::before` —
 *     names a condition the bare block does not cover;
 *   - a **combinator or descendant** — `.row + .row`, `.table thead th` —
 *     names a relationship, not the element;
 *   - **nesting under an at-rule** — a second `.row` inside `@media` or
 *     `@supports` is the whole point of having the at-rule.
 *
 * So the detector counts only preludes that are exactly one bare class
 * selector, at nesting depth zero. Anything with a comma, a colon, a bracket, a
 * space, a second class or an `@` is somebody's deliberate layering and is not
 * this file's business.
 */
import { readdirSync, readFileSync } from "node:fs"
import { dirname, join, relative, sep } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"

/* `src/`, from this file rather than from the process's working directory: a
   test that only passes when it is run from the package root is a test that
   fails in CI for a reason that has nothing to do with the product. */
const SRC = join(dirname(fileURLToPath(import.meta.url)), "..", "..")

/**
 * Every CSS Module under `src/`.
 *
 * `withFileTypes` rather than a `statSync` per entry: on Windows the stat call
 * is a separate syscall through the filter drivers, and walking this tree that
 * way costs about four seconds — enough on its own to time the test out. The
 * directory read already knows what each entry is.
 */
function modules(root: string): string[] {
  const found: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name)
    if (entry.isDirectory()) {
      if (entry.name !== "node_modules") {
        found.push(...modules(path))
      }
    } else if (entry.name.endsWith(".module.css")) {
      found.push(path)
    }
  }
  return found
}

const COMMENTS = /\/\*[\s\S]*?\*\//g

/**
 * The prelude of every block written at the top level of `css`.
 *
 * A regex cannot do this: `/([^{}]+)\{/` cannot tell a top-level rule from one
 * nested inside `@media`, and telling them apart is the whole distinction this
 * file rests on. So it walks the text, counting braces and stepping over
 * strings (a `content: "}"` would otherwise close a block that is still open).
 * At depth zero it accumulates the prelude; everything inside a block is
 * skipped, which is exactly what makes a nested rule invisible here.
 */
function topLevelPreludes(css: string): string[] {
  const found: string[] = []
  let depth = 0
  let prelude = ""

  for (let index = 0; index < css.length; index++) {
    const char = css[index]

    if (char === '"' || char === "'") {
      let text = char
      index++
      while (index < css.length) {
        text += css[index]
        if (css[index] === "\\") {
          index++
          if (index < css.length) {
            text += css[index]
          }
        } else if (css[index] === char) {
          break
        }
        index++
      }
      if (depth === 0) {
        prelude += text
      }
      continue
    }

    if (char === "{") {
      if (depth === 0) {
        found.push(prelude.trim())
        prelude = ""
      }
      depth++
      continue
    }

    if (char === "}") {
      depth = Math.max(0, depth - 1)
      if (depth === 0) {
        prelude = ""
      }
      continue
    }

    // `@import "…";` and friends end without a block; their prelude is not one.
    if (char === ";" && depth === 0) {
      prelude = ""
      continue
    }

    if (depth === 0) {
      prelude += char
    }
  }

  return found
}

/** A prelude that is one class and nothing else — no state, no relation, no group. */
const BARE_CLASS = /^\.[A-Za-z_-][A-Za-z0-9_-]*$/

/** Every class this sheet declares more than one bare top-level block for. */
function shadowedClasses(css: string): string[] {
  const seen = new Map<string, number>()
  for (const prelude of topLevelPreludes(css.replace(COMMENTS, ""))) {
    // `@media`, `@supports`, `@layer`: their contents are skipped entirely.
    if (prelude.startsWith("@")) {
      continue
    }
    // A selector *group* is a deliberate shared base — not a second definition.
    const selectors = prelude.split(",")
    if (selectors.length !== 1) {
      continue
    }
    const selector = selectors[0]?.trim() ?? ""
    if (!BARE_CLASS.test(selector)) {
      continue
    }
    seen.set(selector, (seen.get(selector) ?? 0) + 1)
  }
  return [...seen]
    .filter(([, count]) => count > 1)
    .map(([selector]) => selector)
    .sort()
}

/**
 * The two that already stood on the tree when this guard was written, both in
 * the kit and both benign — `.root` in `combobox-field` carries its box in one
 * block and `position: relative` in another; `.field` in `search-field` carries
 * its layout in one and its height in another. Neither overwrites the other
 * today, and both are the exact shape `.steps` had before it did.
 *
 * They are listed rather than fixed because `shared/ui/**` was being rewritten
 * by another change while this landed. The assertion below is a **subset**
 * check on purpose: merging either pair must not fail the gate for whoever
 * merges it. Deleting the entry afterwards is the follow-up.
 */
const KNOWN: readonly string[] = [
  "shared/ui/combobox-field/combobox-field.module.css :: .root",
  "shared/ui/search-field/search-field.module.css :: .field",
]

describe("a CSS Module declares each class once", () => {
  it("has no bare top-level block that shadows another", () => {
    const found: string[] = []
    for (const path of modules(SRC)) {
      const sheet = readFileSync(path, "utf8")
      for (const selector of shadowedClasses(sheet)) {
        found.push(`${relative(SRC, path).split(sep).join("/")} :: ${selector}`)
      }
    }
    // Subset, not equality — see `KNOWN`. A sheet that is not on that list and
    // declares one class twice at the top level fails here, named.
    expect(found.filter((entry) => !KNOWN.includes(entry))).toEqual([])
  })
})

/* ------------------------------------------------------------------ *
 * The detector itself, held to the distinction it exists to make.
 *
 * A guard that fires on `.columns, .row` would be turned off within a week, so
 * the legal shapes are pinned here rather than left to the tree to prove.
 * ------------------------------------------------------------------ */

describe("the detector tells a second definition from a second layer", () => {
  it("catches two bare blocks for one class", () => {
    expect(
      shadowedClasses(".steps { gap: 1px; }\n.steps { display: grid; }")
    ).toEqual([".steps"])
  })

  it("leaves a group and its later refinement alone", () => {
    // `attention-list.module.css`'s shape: a shared base, then one member of it
    // saying something more.
    expect(
      shadowedClasses(".columns, .row { gap: 1px; }\n.row { flex: 1; }")
    ).toEqual([])
  })

  it("leaves states, attributes and pseudo-elements alone", () => {
    expect(
      shadowedClasses(
        ".item { color: red; }\n" +
          ".item:hover { color: blue; }\n" +
          ".item[data-active] { color: green; }\n" +
          ".item::before { content: '·'; }"
      )
    ).toEqual([])
  })

  it("leaves combinators and descendants alone", () => {
    expect(
      shadowedClasses(
        ".row { gap: 1px; }\n.row + .row { margin: 0; }\n.row thead th { padding: 0; }"
      )
    ).toEqual([])
  })

  it("leaves a compound selector alone", () => {
    // `.row.dense` is a different element than `.row`, not a second `.row`.
    expect(
      shadowedClasses(".row { gap: 1px; }\n.row.dense { gap: 0; }")
    ).toEqual([])
  })

  it("does not descend into at-rules", () => {
    expect(
      shadowedClasses(
        ".row { gap: 1px; }\n@media (max-width: 40rem) {\n  .row { gap: 0; }\n}"
      )
    ).toEqual([])
    expect(
      shadowedClasses(
        ".row { gap: 1px; }\n@supports (display: grid) {\n  .row { display: grid; }\n}"
      )
    ).toEqual([])
  })

  it("does not read a nested rule as a second top-level block", () => {
    expect(
      shadowedClasses(".card { color: red;\n  .card { color: blue; }\n}")
    ).toEqual([])
  })

  it("is not fooled by a brace inside a string", () => {
    // An unbalanced opening brace inside `content` would push the depth
    // counter one too deep, the first block would never close, and the second
    // `.mark` would read as nested — a silent false negative, which is the
    // worst thing a guard can be.
    expect(
      shadowedClasses(".mark { content: '{'; }\n.mark { color: red; }")
    ).toEqual([".mark"])
  })

  it("ignores a repetition that only a comment made look like one", () => {
    expect(
      shadowedClasses("/* .row { gap: 0 } */\n.row { gap: 1px; }")
    ).toEqual([])
  })
})
