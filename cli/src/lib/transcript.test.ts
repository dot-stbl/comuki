/**
 * Transcript flattening tests: blocks → lines order, the plan card,
 * collapsed/expanded event lines, the ctrl+o hint derivation, spacing
 * at block seams, the typing spinner + live tail, notices, and the
 * ANSI-aware width wrap that keeps the fixed-height viewport honest.
 */
import { describe, expect, test } from "bun:test"
import {
  EXPAND_HINT,
  expandHintLine,
  flattenTranscript,
  hasCollapsedThinking,
  liveLines,
  LIVE_CURSOR,
  typingLine,
  wrapVisible,
  type TranscriptSnapshot,
} from "./transcript"
import { colors, paint, stripAnsi } from "../theme"
import type { ChatMessageView } from "./client"
import type { ChatBlock } from "./sessions"

function message(
  id: string,
  role: string,
  content: string,
  parts: ChatMessageView["parts"] = null
): ChatMessageView {
  return {
    id,
    role,
    content,
    toolName: null,
    parts,
    meta: null,
    createdAt: "2026-01-01T00:00:00Z",
  }
}

function snapshot(
  overrides: Partial<TranscriptSnapshot> = {}
): TranscriptSnapshot {
  return {
    blocks: [],
    awaitingApproval: false,
    pendingPlan: null,
    thinking: false,
    liveText: "",
    ...overrides,
  }
}

describe("wrapVisible", () => {
  test("a fitting line passes through untouched", () => {
    expect(wrapVisible("hello", 20)).toEqual(["hello"])
  })

  test("an empty line stays one empty chunk", () => {
    expect(wrapVisible("", 10)).toEqual([""])
  })

  test("a long plain line splits at exact width", () => {
    const chunks = wrapVisible("abcdefghij", 4)
    expect(chunks).toEqual(["abcd", "efgh", "ij"])
  })

  test("SGR sequences are zero-width and colors survive the wrap", () => {
    const painted = paint("abcdefgh", colors.red)
    const chunks = wrapVisible(painted, 4)
    expect(chunks.length).toBe(2)
    expect(stripAnsi(chunks[0] ?? "")).toBe("abcd")
    expect(stripAnsi(chunks[1] ?? "")).toBe("efgh")
    // The continuation re-applies the color active at the break.
    expect(chunks[1]).toContain(colors.red)
    // Each chunk closes its own SGR run.
    expect(chunks[0]).toContain(colors.reset)
    expect(chunks[1]).toContain(colors.reset)
  })

  test("reset drops the carried state for later chunks", () => {
    const line = paint("ab", colors.red) + "cdef"
    const chunks = wrapVisible(line, 2)
    expect(stripAnsi(chunks[0] ?? "")).toBe("ab")
    // After the reset, the continuation is plain — no leaked color.
    expect(chunks[1]).toBe("cd")
    expect(chunks[1]).not.toContain(colors.red)
  })

  test("non-positive width leaves the line alone", () => {
    expect(wrapVisible("abcdefgh", 0)).toEqual(["abcdefgh"])
    expect(wrapVisible("abcdefgh", -1)).toEqual(["abcdefgh"])
  })
})

describe("liveLines", () => {
  test("empty stream renders nothing", () => {
    expect(liveLines("", 60)).toEqual([])
    expect(liveLines("   \n  ", 60)).toEqual([])
  })

  test("cursor rides the last rendered line", () => {
    const lines = liveLines("# Title\n\nbody text", 60)
    expect(lines.length).toBeGreaterThan(0)
    const last = lines[lines.length - 1] ?? ""
    expect(last).toContain(LIVE_CURSOR)
    expect(lines.slice(0, -1).some((line) => line.includes(LIVE_CURSOR))).toBe(
      false
    )
  })
})

describe("typingLine", () => {
  test("spinner frame + label", () => {
    const line = typingLine(0)
    expect(stripAnsi(line)).toContain("comuki thinking")
    expect(stripAnsi(line)).toContain("⠋")
  })

  test("frame advances within the palette", () => {
    const line = typingLine(2)
    expect(stripAnsi(line)).toContain("⠹")
  })
})

describe("hasCollapsedThinking / expandHintLine", () => {
  const blocks: readonly ChatBlock[] = [
    {
      kind: "message",
      key: "a",
      message: message("m1", "assistant", "", [
        { kind: "thinking", text: "hidden", tokens: 5 },
        { kind: "text", markdown: "answer" },
      ]),
    },
  ]

  test("collapsed thinking parts in the active transcript request the hint", () => {
    expect(hasCollapsedThinking(blocks, false)).toBe(true)
    expect(hasCollapsedThinking(blocks, true)).toBe(false)
  })

  test("no thinking parts — no hint", () => {
    const plain: readonly ChatBlock[] = [
      {
        kind: "message",
        key: "a",
        message: message("m1", "assistant", "answer", [
          { kind: "text", markdown: "answer" },
        ]),
      },
    ]
    expect(hasCollapsedThinking(plain, false)).toBe(false)
  })

  test("the hint renders dim, right-aligned inside the width", () => {
    const line = expandHintLine(80)
    expect(stripAnsi(line)).toBe(
      " ".repeat(80 - EXPAND_HINT.length - 1) + EXPAND_HINT
    )
    expect(line).toContain(colors.dim)
    expect(stripAnsi(line).length).toBeLessThanOrEqual(80)
  })
})

describe("flattenTranscript", () => {
  test("no session → notices only", () => {
    expect(flattenTranscript(undefined, 60, 0, ["notice"])).toEqual(["notice"])
  })

  test("empty session renders nothing", () => {
    expect(flattenTranscript(snapshot(), 60, 0)).toEqual([])
  })

  test("message blocks and line blocks keep transcript order", () => {
    const blocks: readonly ChatBlock[] = [
      { kind: "message", key: "a", message: message("m1", "user", "hi there") },
      {
        kind: "lines",
        key: "b",
        lines: ["  raw line one", "  raw line two"],
      },
      {
        kind: "message",
        key: "c",
        message: message("m2", "assistant", "answer body"),
      },
    ]
    const lines = flattenTranscript(snapshot({ blocks }), 80, 0)
    const plain = lines.map(stripAnsi)
    expect(plain.some((line) => line.includes("hi there"))).toBe(true)
    // The user echo is bare bold text — no › prefix anywhere.
    expect(plain.some((line) => line.trimStart().startsWith("›"))).toBe(false)
    expect(plain).toContain("  raw line one")
    expect(plain).toContain("  raw line two")
    expect(plain.some((line) => line.includes("answer body"))).toBe(true)
    // Order: user echo first, assistant last.
    expect(plain.indexOf("  raw line one")).toBeLessThan(
      plain.findIndex((line) => line.includes("answer body"))
    )
  })

  test("thinking/tool parts collapse to ⏺ event lines by default", () => {
    const blocks: readonly ChatBlock[] = [
      {
        kind: "message",
        key: "a",
        message: message("m1", "assistant", "", [
          { kind: "thinking", text: "secret reasoning", tokens: 4_100, durationMs: 6_200 },
          {
            kind: "tool",
            name: "memory.recall",
            inputJson: `{"q":"identity module","limit":5}`,
            status: "succeeded",
            durationMs: 41,
          },
          { kind: "text", markdown: "the answer" },
        ]),
      },
    ]
    const lines = flattenTranscript(snapshot({ blocks }), 90, 0)
    const plain = lines.map(stripAnsi)
    // Assistant rows carry the one-space gutter over the event indent.
    expect(plain).toContain("   ⏺ thinking · 4.1k tok · 6.2s")
    expect(plain).toContain(`   ⏺ memory.recall("identity module", 5)  ok 41ms`)
    expect(plain.join("\n")).not.toContain("secret reasoning")
    expect(plain.some((line) => line.includes("the answer"))).toBe(true)
  })

  test("expanded flag reveals the thinking body under the event line", () => {
    const blocks: readonly ChatBlock[] = [
      {
        kind: "message",
        key: "a",
        message: message("m1", "assistant", "", [
          { kind: "thinking", text: "visible reasoning", tokens: 5 },
        ]),
      },
    ]
    const lines = flattenTranscript(
      snapshot({ blocks, expanded: true }),
      80,
      0
    )
    const plain = lines.map(stripAnsi)
    expect(plain).toContain("   ⏺ thinking · 5 tok")
    expect(plain).toContain("     visible reasoning")
  })

  test("block seams carry exactly one blank line — never two", () => {
    const blocks: readonly ChatBlock[] = [
      { kind: "message", key: "a", message: message("m1", "user", "one") },
      // The user echo already ends with a blank; the next block starts
      // with one too — the seam must collapse to a single blank.
      { kind: "lines", key: "b", lines: ["", "  raw"] },
      { kind: "message", key: "c", message: message("m2", "assistant", "answer") },
    ]
    const lines = flattenTranscript(snapshot({ blocks }), 80, 0)
    for (let index = 1; index < lines.length; index += 1) {
      const previousBlank = stripAnsi(lines[index - 1] ?? "").length === 0
      const currentBlank = stripAnsi(lines[index] ?? "").length === 0
      expect(previousBlank && currentBlank).toBe(false)
    }
    expect(lines[0]).not.toBe("")
  })

  test("awaiting approval appends the framed plan card and the hint", () => {
    const plan = {
      nodes: [
        {
          key: "n1",
          profileKey: "default",
          brief: "do things\nmore",
          dependsOn: [],
        },
      ],
    }
    const lines = flattenTranscript(
      snapshot({ awaitingApproval: true, pendingPlan: plan }),
      80,
      0
    )
    const plain = lines.map(stripAnsi)
    expect(plain.some((line) => line.includes("┌─ plan · 1 шаг "))).toBe(true)
    expect(plain.some((line) => line.includes("do things"))).toBe(true)
    expect(plain[plain.length - 1]).toContain("approve · reject [reason]")
  })

  test("thinking appends the spinner and the live tail with cursor", () => {
    const lines = flattenTranscript(
      snapshot({ thinking: true, liveText: "streaming **bold**" }),
      60,
      2
    )
    const plain = lines.map(stripAnsi)
    expect(plain.some((line) => line.includes("comuki thinking"))).toBe(true)
    expect(plain.some((line) => line.includes("streaming"))).toBe(true)
    const last = lines[lines.length - 1] ?? ""
    expect(last).toContain(LIVE_CURSOR)
  })

  test("notices land after the transcript", () => {
    const blocks: readonly ChatBlock[] = [
      { kind: "lines", key: "a", lines: ["block"] },
    ]
    const lines = flattenTranscript(snapshot({ blocks }), 60, 0, ["tail notice"])
    expect(lines[lines.length - 1]).toBe("tail notice")
    expect(lines).toContain("block")
  })

  test("overly wide lines are wrapped to the viewport width", () => {
    const blocks: readonly ChatBlock[] = [
      {
        kind: "lines",
        key: "a",
        lines: [paint("x".repeat(30), colors.red)],
      },
    ]
    const lines = flattenTranscript(snapshot({ blocks }), 10, 0)
    expect(lines.length).toBe(3)
    for (const line of lines) {
      expect(stripAnsi(line).length).toBeLessThanOrEqual(10)
    }
    // The color state carries into every continuation chunk.
    expect(lines.every((line) => line.includes(colors.red))).toBe(true)
  })
})
