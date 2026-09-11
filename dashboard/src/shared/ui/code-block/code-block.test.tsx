import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { fireEvent, render, waitFor } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { CodeBlock } from "./code-block"
import { resolveLanguage } from "./highlight"

/**
 * The code block's four promises, and the one the stylesheet has to keep.
 *
 * Three of them are behaviour a test can see — the chip names the grammar,
 * the fold says how much is behind it, the wrap toggle is the operator's.
 * The fourth is that **highlighting never carries a colour of its own**, and
 * that one is only checkable by reading the stylesheet back off disk, the way
 * `data-table.test.tsx` reads its own: jsdom computes no styles, so a hex
 * quietly added to a `hljs-` rule would be invisible to every rendered case
 * here and would break six of the seven themes in the browser.
 */

const SHEET = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "code-block.module.css"),
  "utf8"
)

/* Addressed by `data-test`, the attribute this product stamps — it is not
   testing-library's `data-testid` and `getByTestId` would find nothing. */
const at = (name: string) =>
  document.querySelector<HTMLElement>(`[data-test="${name}"]`)

const lines = (count: number) =>
  Array.from({ length: count }, (_, index) => `const n${index} = ${index}`).join(
    "\n"
  )

describe("the language chip", () => {
  it("names the grammar the block is actually being read with", () => {
    render(<CodeBlock source="const a = 1" language="typescript" />)
    expect(at("code-block-language")?.textContent).toBe("ts")
    expect(at("code-block")?.getAttribute("data-language")).toBe(
      "ts"
    )
  })

  it("says `text` for a language nobody registered, rather than claiming one", () => {
    render(<CodeBlock source="+-[>+<]" language="brainfuck" />)
    expect(at("code-block-language")?.textContent).toBe("text")
  })

  it("resolves the spellings a fence is actually written with", () => {
    expect(resolveLanguage("c#")).toBe("cs")
    expect(resolveLanguage("YML")).toBe("yaml")
    expect(resolveLanguage("language-sh")).toBe("bash")
    // JavaScript rides the TypeScript grammar — a superset, and not a tenth
    // grammar to load.
    expect(resolveLanguage("javascript")).toBe("ts")
    expect(resolveLanguage("cobol")).toBeNull()
    expect(resolveLanguage(undefined)).toBeNull()
  })
})

describe("the code itself", () => {
  it("is on screen before — and whether or not — the highlighter arrives", () => {
    render(<CodeBlock source="const answer = 42" language="ts" />)
    expect(at("code-block-body")?.textContent).toContain(
      "const answer = 42"
    )
  })

  it("is highlighted into classes, never into inline colour", async () => {
    render(<CodeBlock source={'const answer = "42"'} language="ts" />)

    const body = await waitFor(() => {
      const found = (at("code-block-body") as HTMLElement)
      expect(found.querySelector(".hljs-string")).not.toBeNull()
      return found
    })

    // The whole reason it is highlight.js and not shiki: the markup carries a
    // class the theme styles, not a hex the theme cannot reach.
    expect(body.innerHTML).not.toMatch(/style="[^"]*color/i)
    expect(body.textContent).toContain('const answer = "42"')
  })

  it("drops the trailing newline a fence always carries", () => {
    render(<CodeBlock source={"one\ntwo\n\n"} />)
    expect(at("code-block-body")?.textContent).toBe("one\ntwo")
  })
})

describe("the fold", () => {
  it("leaves a short block open and offers no control", () => {
    render(<CodeBlock source={lines(8)} language="ts" />)
    expect(at("code-block")?.getAttribute("data-collapsed")).toBe(
      null
    )
    expect(at("code-block-expand")).toBeNull()
  })

  it("folds a long one and says how many lines are behind it", () => {
    render(<CodeBlock source={lines(62)} language="ts" />)

    expect(at("code-block")?.getAttribute("data-collapsed")).toBe(
      "true"
    )
    const control = (at("code-block-expand") as HTMLElement)
    expect(control.textContent).toContain("show all 62 lines")
    expect(control.getAttribute("aria-expanded")).toBe("false")
  })

  it("opens on a press and offers the way back", () => {
    render(<CodeBlock source={lines(62)} language="ts" />)

    fireEvent.click((at("code-block-expand") as HTMLElement))

    expect(at("code-block")?.getAttribute("data-collapsed")).toBe(
      null
    )
    expect(at("code-block-expand")?.textContent).toContain(
      "show less"
    )
  })

  it("takes the call site's threshold and publishes it as the height", () => {
    render(<CodeBlock source={lines(10)} language="ts" collapseAfter={4} />)
    const block = (at("code-block") as HTMLElement)

    expect(block.getAttribute("data-collapsed")).toBe("true")
    expect(block.style.getPropertyValue("--code-lines")).toBe("4")
  })
})

describe("wrapping", () => {
  it("is off by default, so a long line scrolls instead of reflowing", () => {
    render(<CodeBlock source={"a".repeat(400)} language="bash" />)
    expect(at("code-block")?.getAttribute("data-wrap")).toBe(null)
    expect(at("code-block-wrap")?.getAttribute("aria-pressed")).toBe(
      "false"
    )
  })

  it("is one press away, and says it is on", () => {
    render(<CodeBlock source={"a".repeat(400)} language="bash" />)

    fireEvent.click((at("code-block-wrap") as HTMLElement))

    expect(at("code-block")?.getAttribute("data-wrap")).toBe("true")
    expect(at("code-block-wrap")?.getAttribute("aria-pressed")).toBe(
      "true"
    )
  })
})

describe("the origin line", () => {
  it("is absent when nothing said where the code came from", () => {
    render(<CodeBlock source="const a = 1" />)
    expect(at("code-block-origin")).toBeNull()
  })

  it("reads `path:line` when both are known, and the path alone when one is", () => {
    const { rerender } = render(
      <CodeBlock source="const a = 1" path="src/a.ts" startLine={128} />
    )
    expect(at("code-block-origin")?.textContent).toBe(
      "src/a.ts:128"
    )

    rerender(<CodeBlock source="const a = 1" path="src/a.ts" />)
    expect(at("code-block-origin")?.textContent).toBe("src/a.ts")
  })
})

describe("copying", () => {
  it("offers the kit's own control, over the source without its fence newline", () => {
    render(<CodeBlock source={"one\ntwo\n"} />)
    expect((at("code-block-copy") as HTMLElement)).not.toBeNull()
  })
})

describe("the stylesheet carries no colour of its own", () => {
  it("states no hex anywhere", () => {
    // `DESIGN.md`: a colour belongs to a theme in the registry, never to a
    // component. A syntax palette is the one place that rule is tempting to
    // break, so it is the one place it is asserted.
    expect(SHEET.match(/#[0-9a-f]{3,8}\b/gi)).toBeNull()
  })

  it("resolves every syntax role through a token", () => {
    const roles = SHEET.matchAll(/--code-[\w-]+:\s*([^;]+);/g)
    const values = [...roles].map(([, value]) => (value ?? "").trim())

    expect(values.length).toBeGreaterThan(4)
    for (const value of values) {
      // The two unitless geometry roles are counts, not colours.
      if (/^[\d.]+$/.test(value)) {
        continue
      }
      expect(value).toContain("var(--")
    }
  })

  it("names no font family of its own", () => {
    const families = [...SHEET.matchAll(/font-family:\s*([^;]+);/g)].map(
      ([, value]) => (value ?? "").trim()
    )
    expect(families.length).toBeGreaterThan(0)
    for (const family of families) {
      expect(family === "inherit" || family.includes("var(--")).toBe(true)
    }
  })
})
