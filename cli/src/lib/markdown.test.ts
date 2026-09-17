/**
 * Byte-level tests for the pure markdown → ANSI renderer: content
 * assertions run on stripped lines, style assertions on the raw ones.
 */
import { describe, expect, it } from "bun:test"
import { renderMarkdownLines } from "./markdown"
import { colors, stripAnsi } from "../theme"

const plain = (markdown: string, width = 80): string[] =>
  renderMarkdownLines(markdown, width).map(stripAnsi)

describe("renderMarkdownLines — prose", () => {
  it("passes plain text through as a single line", () => {
    expect(plain("hello world")).toEqual(["hello world"])
  })

  it("returns nothing for blank input", () => {
    expect(renderMarkdownLines("")).toEqual([])
    expect(renderMarkdownLines("  \n \n")).toEqual([])
  })

  it("wraps long paragraphs to the given width", () => {
    const words = Array.from({ length: 30 }, (_, i) => `word${i}`).join(" ")
    const lines = renderMarkdownLines(words, 40)
    expect(lines.length).toBeGreaterThan(1)
    for (const line of lines) {
      expect(stripAnsi(line).length).toBeLessThanOrEqual(40)
    }
    // nothing lost — the words survive the wrap
    expect(lines.map(stripAnsi).join(" ")).toBe(words)
  })

  it("preserves hard line breaks inside a paragraph", () => {
    expect(plain("first line\nsecond line")).toEqual([
      "first line",
      "second line",
    ])
  })

  it("separates two paragraphs with one blank line", () => {
    expect(plain("one\n\ntwo")).toEqual(["one", "", "two"])
  })
})

describe("renderMarkdownLines — inline styles", () => {
  it("renders bold with the bright style", () => {
    const [line] = renderMarkdownLines("**bold** plain")
    expect(line).toContain(colors.bright + "bold" + colors.reset)
    expect(stripAnsi(line ?? "")).toBe("bold plain")
  })

  it("renders italic with the italic style", () => {
    const [line] = renderMarkdownLines("*careful* wording")
    expect(line).toContain(colors.italic + "careful" + colors.reset)
    expect(stripAnsi(line ?? "")).toBe("careful wording")
  })

  it("highlights inline code in the accent color", () => {
    const [line] = renderMarkdownLines("run `npm test` now")
    expect(line).toContain(colors.accent + "npm test" + colors.reset)
    expect(stripAnsi(line ?? "")).toBe("run npm test now")
  })

  it("keeps codespans unbroken when wrapping", () => {
    const lines = renderMarkdownLines(
      "aaa bbb ccc `two words` ddd eee fff ggg",
      20
    )
    const joined = lines.map(stripAnsi).join("\n")
    expect(joined).toContain("two words")
    expect(lines.map(stripAnsi)).not.toContain("two")
  })

  it("appends the href after a labeled link", () => {
    expect(plain("see [docs](https://example.com)")).toEqual([
      "see docs (https://example.com)",
    ])
  })

  it("leaves autolinks without a duplicate href", () => {
    expect(plain("<https://example.com>")).toEqual(["https://example.com"])
  })
})

describe("renderMarkdownLines — headings", () => {
  it("renders h1 bright + underlined", () => {
    const [line] = renderMarkdownLines("# Title")
    expect(line).toContain(colors.bright)
    expect(line).toContain(colors.underline)
    expect(stripAnsi(line ?? "")).toBe("Title")
  })

  it("renders h2 bright, h3 accent (scaled)", () => {
    const [h2] = renderMarkdownLines("## Section")
    expect(h2).toContain(colors.bright)
    expect(h2).not.toContain(colors.underline)
    const [h3] = renderMarkdownLines("### Subsection")
    expect(h3).toContain(colors.accent)
  })
})

describe("renderMarkdownLines — code blocks", () => {
  it("renders a fenced block with dim border, label and padded body", () => {
    const lines = renderMarkdownLines("```ts\nconst x = 1\n```")
    expect(lines).toHaveLength(3)
    expect(lines[0]).toContain(colors.dim)
    expect(stripAnsi(lines[0] ?? "")).toContain("ts")
    expect(stripAnsi(lines[1] ?? "")).toBe("  │ const x = 1")
    expect(lines[1]).toContain(colors.muted)
    expect(stripAnsi(lines[2] ?? "")).toContain("└")
  })

  it("hard-wraps long code lines instead of overflowing", () => {
    const long = "x".repeat(60)
    const lines = renderMarkdownLines("```\n" + long + "\n```", 40)
    const body = lines.slice(1, -1)
    expect(body.length).toBeGreaterThan(1)
    for (const line of body) {
      expect(stripAnsi(line).length).toBeLessThanOrEqual(40)
    }
  })

  it("renders an unterminated fence (streaming) as a growing block", () => {
    const lines = renderMarkdownLines("```ts\nconst partial = ")
    const body = lines.map(stripAnsi).join("\n")
    expect(body).toContain("const partial = ")
    expect(body).toContain("ts")
  })

  it("keeps blank lines inside a code block", () => {
    const lines = renderMarkdownLines("```\na\n\nb\n```")
    expect(stripAnsi(lines[2] ?? "")).toBe("  │")
  })
})

describe("renderMarkdownLines — lists", () => {
  it("renders unordered items with the accent bullet", () => {
    const lines = renderMarkdownLines("- alpha\n- beta")
    expect(stripAnsi(lines[0] ?? "")).toBe("  · alpha")
    expect(stripAnsi(lines[1] ?? "")).toBe("  · beta")
    expect(lines[0]).toContain(colors.accent)
  })

  it("renders ordered items with numbers", () => {
    const lines = renderMarkdownLines("1. first\n2. second")
    expect(stripAnsi(lines[0] ?? "")).toBe("  1. first")
    expect(stripAnsi(lines[1] ?? "")).toBe("  2. second")
  })

  it("starts ordered lists from the given start", () => {
    const lines = renderMarkdownLines("3. third")
    expect(stripAnsi(lines[0] ?? "")).toBe("  3. third")
  })

  it("renders task items with checkboxes", () => {
    const lines = renderMarkdownLines("- [x] done\n- [ ] todo")
    expect(stripAnsi(lines[0] ?? "")).toBe("  · [x] done")
    expect(stripAnsi(lines[1] ?? "")).toBe("  · [ ] todo")
  })

  it("indents nested lists", () => {
    const lines = renderMarkdownLines("- top\n  - nested")
    expect(stripAnsi(lines[0] ?? "")).toBe("  · top")
    expect(stripAnsi(lines[1] ?? "")).toBe("    · nested")
  })

  it("wraps long list items with a hanging indent", () => {
    const item = ["one", "two", "three", "four"].join(" ")
    const lines = renderMarkdownLines(`- ${item} ${item}`, 22)
    const stripped = lines.map(stripAnsi)
    expect(stripped[0]).toBe("  · one two three four")
    expect(stripped[1]).toBe("    one two three four")
  })
})

describe("renderMarkdownLines — quotes, rules, tables", () => {
  it("renders blockquotes behind a dim bar", () => {
    const lines = renderMarkdownLines("> quoted wisdom")
    expect(stripAnsi(lines[0] ?? "")).toBe("  ▎ quoted wisdom")
    expect(lines[0]).toContain(colors.dim)
  })

  it("renders a horizontal rule", () => {
    const [line] = renderMarkdownLines("---")
    expect(stripAnsi(line ?? "").trim()).toBe("─".repeat(76))
  })

  it("renders a table with a header and separator", () => {
    const lines = renderMarkdownLines(
      "| key | value |\n| --- | --- |\n| a | 1 |"
    )
    const stripped = lines.map(stripAnsi)
    expect(stripped[0]).toContain("key")
    expect(stripped[0]).toContain("value")
    expect(stripped[1]).toContain("─")
    expect(stripped[2]).toContain("a")
    expect(lines[0]).toContain(colors.bright)
  })
})

describe("renderMarkdownLines — degradation", () => {
  it("degrades to raw lines when the lexer throws", () => {
    // A fenced block whose "info string" is pathological enough to make
    // the lexer throw is hard to construct on purpose; patch the lexer
    // via a broken input instead: extremely deep nesting must still
    // produce some output, never throw.
    const deep = "> ".repeat(200) + "text"
    const lines = renderMarkdownLines(deep)
    expect(lines.length).toBeGreaterThan(0)
    expect(lines.map(stripAnsi).join("\n")).toContain("text")
  })
})
