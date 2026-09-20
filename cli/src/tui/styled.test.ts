/**
 * Styled-line primitives (issue #74) — pure unit tests over the
 * markdown/diff/wrap ports: no renderer, no i18n, segment-level
 * assertions on text, tone and flags.
 */

import { describe, expect, test } from "bun:test"
import {
  blankLine,
  codeFrameLines,
  diffLines,
  hardWrap,
  isDiffContent,
  lineLength,
  lineText,
  markdownLines,
  seg,
  truncateTail,
  wrapSegments,
} from "./styled"

function tonesOf(line: readonly ReturnType<typeof seg>[]): string[] {
  return line.map((segment) => segment.tone)
}

describe("wrapSegments — the wrapRuns port", () => {
  test("words re-join with spaces on one line", () => {
    const lines = wrapSegments([seg("queue first bold")], 40)
    expect(lines).toHaveLength(1)
    expect(lineText(lines[0]!)).toBe("queue first bold")
  })

  test("wraps to the continuation prefix at the width", () => {
    // room = width - prefix length = 5 → one word per continuation row.
    const lines = wrapSegments(
      [seg("aaa bbb ccc ddd")],
      7,
      [seg("p ", "muted")],
      [seg("  ", "muted")]
    )
    expect(lines.map(lineText)).toEqual(["p aaa", "  bbb", "  ccc", "  ddd"])
  })

  test("a lone overlong word hard-splits by characters", () => {
    const lines = wrapSegments([seg("abcdefgh")], 3)
    expect(lines.map(lineText)).toEqual(["abc", "def", "gh"])
  })

  test("segments keep their tones through the wrap", () => {
    const lines = wrapSegments([seg("plain "), seg("code", "accent")], 40)
    expect(lines).toHaveLength(1)
    // plain, joining space (takes the following unit's tone), code.
    expect(tonesOf(lines[0]!)).toEqual(["text", "accent", "accent"])
    expect(lineText(lines[0]!)).toBe("plain code")
  })

  test("atomic segments stay whole", () => {
    const lines = wrapSegments(
      [seg("word "), seg("a-very-long-code-span", "accent", {}, true)],
      10
    )
    expect(lines.map(lineText)).toEqual(["word", "a-very-long-code-span"])
  })

  test("hard break segments flush the current line", () => {
    const lines = wrapSegments([seg("one"), seg("\n", "text", {}, true), seg("two")], 40)
    expect(lines.map(lineText)).toEqual(["one", "two"])
  })
})

describe("hardWrap + truncateTail", () => {
  test("character wrap with continuation alignment", () => {
    expect(hardWrap("abcdefgh", 3)).toEqual(["abc", "def", "gh"])
    expect(hardWrap("", 3)).toEqual([""])
  })

  test("tail truncation keeps the tail and marks the cut", () => {
    expect(truncateTail("short", 10)).toBe("short")
    expect(truncateTail("0123456789abcdef", 5)).toBe("…cdef")
  })
})

describe("markdownLines — the renderMarkdownLines port", () => {
  test("paragraphs wrap at the width; blocks separate by one blank line", () => {
    const lines = markdownLines("first paragraph\n\nsecond paragraph", 40)
    expect(lines.map(lineText)).toEqual([
      "first paragraph",
      blankLine()[0]!.text,
      "second paragraph",
    ])
  })

  test("h1 is bold+underlined, h2 bold, h3+ accent", () => {
    const [h1] = markdownLines("# Title", 40)
    expect(h1![0]!.style).toMatchObject({ bold: true, underline: true })

    const [h2] = markdownLines("## Sub", 40)
    expect(h2![0]!.style.bold).toBe(true)

    const [h3] = markdownLines("### Deep", 40)
    expect(h3![0]!.tone).toBe("accent")
    expect(h3![0]!.style.bold).toBeUndefined()
  })

  test("inline code wears the accent tone", () => {
    const [line] = markdownLines("run `npm test` now", 40)
    const code = line!.find((segment) => segment.text === "npm test")
    expect(code?.tone).toBe("accent")
  })

  test("bullets render the accent marker; ordered markers are muted", () => {
    const lines = markdownLines("- one\n- two", 40)
    expect(lines).toHaveLength(2)
    expect(lines[0]!.map((segment) => segment.text).join("")).toContain(". one")
    expect(lines[0]![0]!.text).toBe("  ")
    expect(lines[0]!.some((segment) => segment.tone === "accent")).toBe(true)

    const ordered = markdownLines("1. first", 40)
    expect(ordered[0]!.some((segment) => segment.tone === "muted")).toBe(true)
  })

  test("fenced code renders the dim frame with the language label", () => {
    const lines = markdownLines("```ts\nconst a = 1\n```", 40)
    const frame = lineText(lines[0]!)
    expect(frame.startsWith("  +- ts ")).toBe(true)
    expect(frame.endsWith("+")).toBe(true)
    expect(lineText(lines[1]!)).toBe("  | const a = 1")
    expect(lines[2]!.every((segment) => segment.tone === "rule")).toBe(true)
  })

  test("blockquote bars the content with a muted pipe", () => {
    const lines = markdownLines("> quoted", 40)
    expect(lineText(lines[0]!)).toBe("  | quoted")
    expect(lines[0]![0]!.tone).toBe("muted")
  })

  test("blank input renders nothing", () => {
    expect(markdownLines("   \n  ", 40)).toEqual([])
  })
})

describe("codeFrameLines — narrow bodies hard-wrap inside the frame", () => {
  test("long body lines wrap without breaking the frame math", () => {
    const lines = codeFrameLines("ts", "x".repeat(50), 20)
    const bodyWidth = Math.max(...lines.slice(1, -1).map(lineLength))
    expect(bodyWidth).toBeLessThanOrEqual(20)
  })
})

describe("diff rendering — the lib/diff port", () => {
  test("detection: language tag, git header, hunk header; prose does not flip it", () => {
    expect(isDiffContent("diff", "anything")).toBe(true)
    expect(isDiffContent(null, "diff --git a/x b/x")).toBe(true)
    expect(isDiffContent(null, "--- a/x\n+++ b/x\n@@ -1 +1 @@").valueOf()).toBe(true)
    expect(isDiffContent("ts", "const a = 1")).toBe(false)
    expect(isDiffContent(null, "an @@ in prose")).toBe(false)
  })

  test("+ ok, - error, @@ accent, file headers muted, context faint", () => {
    const lines = diffLines(
      ["diff --git a/x b/x", "index 123..456 100644", "@@ -1,2 +1,2 @@", "-old", "+new", " context"].join("\n"),
      40
    )
    expect(lines[0]![0]!.tone).toBe("muted")
    expect(lines[1]![0]!.tone).toBe("muted")
    expect(lines[2]![0]!.tone).toBe("accent")
    expect(lines[3]![0]!.tone).toBe("error")
    expect(lines[4]![0]!.tone).toBe("ok")
    expect(lines[5]![0]!.tone).toBe("faint")
  })

  test("one diff line is ONE row — the tail ellipsizes, never wraps", () => {
    const long = `+${"x".repeat(60)}`
    const lines = diffLines(long, 20)
    expect(lines).toHaveLength(1)
    expect(lineText(lines[0]!)).toHaveLength(20)
    expect(lineText(lines[0]!).endsWith("…")).toBe(true)
  })
})
