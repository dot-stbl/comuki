/**
 * Structured diff rendering tests: detection (language tag vs content
 * sniffing vs prose false-positives), per-line tier colours, blank-line
 * handling, and the one-row-per-line width contract.
 */
import { describe, expect, test } from "bun:test"
import { isDiffContent, renderDiffLines } from "./diff"
import { colors, stripAnsi } from "../theme"

const UNIFIED = [
  "diff --git a/src/app.ts b/src/app.ts",
  "index 1a2b3c4..5d6e7f8 100644",
  "--- a/src/app.ts",
  "+++ b/src/app.ts",
  "@@ -10,7 +10,8 @@ function run():",
  " context line",
  "-removed line",
  "+added line",
  "+another addition",
  " more context",
].join("\n")

describe("isDiffContent", () => {
  test("the diff language tag wins regardless of body", () => {
    expect(isDiffContent("diff", "+foo\n-bar")).toBe(true)
    expect(isDiffContent(" Diff ", "+foo")).toBe(true)
    expect(isDiffContent("diff", "not a diff at all")).toBe(true)
  })

  test("no tag — sniffs the unified-diff signatures", () => {
    expect(isDiffContent("", UNIFIED)).toBe(true)
    expect(isDiffContent(undefined, UNIFIED)).toBe(true)
    expect(isDiffContent("", "diff --git a/x b/x\n--- a/x\n+++ b/x")).toBe(
      true
    )
    expect(isDiffContent("", "--- a/file.ts\n+++ b/file.ts\n@@ -1 +1 @@")).toBe(
      true
    )
    // A hunk header alone (patch fragment without file headers) counts.
    expect(isDiffContent("text", "@@ -3,7 +3,8 @@ ctx\n-old\n+new")).toBe(true)
  })

  test("prose and plain code never trip the sniffer", () => {
    expect(isDiffContent("ts", "const a = 1\n-- separator\n+++ not diff")).toBe(
      false
    )
    expect(isDiffContent("", "mentions @@decorator and --- dashes in prose")).toBe(
      false
    )
    // `+`/`-` leading lines alone prove nothing.
    expect(isDiffContent("python", "+positive\n-negative")).toBe(false)
    // Content wins over a wrong/absent language tag — the sniff is the
    // safety net for models that fence diffs as `text`.
    expect(isDiffContent("text", UNIFIED)).toBe(true)
  })
})

describe("renderDiffLines", () => {
  const lines = renderDiffLines(UNIFIED, 200)

  test("one painted row per source line", () => {
    expect(lines).toHaveLength(10)
    const plain = lines.map(stripAnsi)
    expect(plain[0]).toContain("diff --git a/src/app.ts")
    expect(plain[4]).toBe("@@ -10,7 +10,8 @@ function run():")
    expect(plain[6]).toBe("-removed line")
    expect(plain[7]).toBe("+added line")
  })

  test("additions lavender, deletions yellow, hunks accent", () => {
    expect(lines[7]).toContain(colors.ok)
    expect(lines[8]).toContain(colors.ok)
    expect(lines[6]).toContain(colors.error)
    expect(lines[4]).toContain(colors.accent)
    // The ok tier is the lavender success colour, never plain text.
    expect(stripAnsi(lines[7] ?? "")).not.toBe(lines[7])
  })

  test("context and file metadata recede", () => {
    expect(lines[0]).toContain(colors.dim)
    expect(lines[2]).toContain(colors.dim)
    expect(lines[5]).toContain(colors.faint)
    expect(lines[9]).toContain(colors.faint)
  })

  test("blank lines stay truly blank", () => {
    const withBlank = renderDiffLines("+a\n\n+b", 40)
    expect(withBlank[1]).toBe("")
  })

  test("a trailing newline does not mint an empty last row", () => {
    expect(renderDiffLines("+a\n", 40)).toEqual([
      `${colors.ok}+a${colors.reset}`,
    ])
  })

  test("overlong lines ellipsize on the tail within width", () => {
    const long = `+${"x".repeat(50)}`
    const rows = renderDiffLines(long, 20)
    expect(rows).toHaveLength(1)
    const plain = stripAnsi(rows[0] ?? "")
    expect(plain.length).toBe(20)
    expect(plain.startsWith("+xxxx")).toBe(true)
    expect(plain.endsWith("…")).toBe(true)
  })

  test("non-positive width leaves lines whole", () => {
    const rows = renderDiffLines(`+${"x".repeat(50)}`, 0)
    expect(stripAnsi(rows[0] ?? "").length).toBe(51)
  })
})
