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
  classifyLine,
  findMatches,
  flattenTranscript,
  hasCollapsedThinking,
  highlightLine,
  liveLines,
  padVisible,
  LIVE_CURSOR,
  nextMatchIndex,
  typingLines,
  wrapVisible,
  type TranscriptSnapshot,
} from "./transcript"
import { colors, paint, stripAnsi } from "../theme"
import { ComukiApiError, type ChatMessageView } from "./client"
import type { ChatBlock } from "./sessions"
import { alertLines } from "./alerts"

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
    const painted = paint("abcdefgh", colors.error)
    const chunks = wrapVisible(painted, 4)
    expect(chunks.length).toBe(2)
    expect(stripAnsi(chunks[0] ?? "")).toBe("abcd")
    expect(stripAnsi(chunks[1] ?? "")).toBe("efgh")
    // The continuation re-applies the color active at the break.
    expect(chunks[1]).toContain(colors.error)
    // Each chunk closes its own SGR run.
    expect(chunks[0]).toContain(colors.reset)
    expect(chunks[1]).toContain(colors.reset)
  })

  test("reset drops the carried state for later chunks", () => {
    const line = paint("ab", colors.error) + "cdef"
    const chunks = wrapVisible(line, 2)
    expect(stripAnsi(chunks[0] ?? "")).toBe("ab")
    // After the reset, the continuation is plain — no leaked color.
    expect(chunks[1]).toBe("cd")
    expect(chunks[1]).not.toContain(colors.error)
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

describe("typingLines", () => {
  test("renders one spinner frame and label", () => {
    const lines = typingLines(0).map(stripAnsi)
    expect(lines).toEqual(["| thinking"])
  })

  test("cycles through the one-line spinner", () => {
    expect(stripAnsi(typingLines(1)[0] ?? "")).toBe("/ thinking")
    expect(stripAnsi(typingLines(2)[0] ?? "")).toBe("- thinking")
    expect(stripAnsi(typingLines(3)[0] ?? "")).toBe("\\ thinking")
    expect(stripAnsi(typingLines(4)[0] ?? "")).toBe("| thinking")
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
    expect(plain.some((line) => line.includes("> hi there"))).toBe(true)
    expect(plain.some((line) => line.trimStart().startsWith("›"))).toBe(false)
    expect(plain).toContain("  raw line one")
    expect(plain).toContain("  raw line two")
    expect(plain.some((line) => line.includes("answer body"))).toBe(true)
    // Order: user echo first, assistant last.
    expect(plain.indexOf("  raw line one")).toBeLessThan(
      plain.findIndex((line) => line.includes("answer body"))
    )
  })

  test("alertLines as a lines block keep the framed card", () => {
    const blocks: readonly ChatBlock[] = [
      {
        kind: "lines",
        key: "alert",
        lines: alertLines(
          new ComukiApiError(
            401,
            "authentication.required",
            "permission 'chat:use' requires a signed-in subject"
          )
        ),
      },
    ]
    const plain = flattenTranscript(snapshot({ blocks }), 80, 0).map(stripAnsi)
    expect(
      plain.some((line) => line.includes("+- error · authentication.required"))
    ).toBe(true)
    expect(plain.some((line) => line.includes("/login"))).toBe(true)
    expect(plain.some((line) => line.includes("✗ HTTP 401"))).toBe(false)
  })

  test("thinking/tool parts collapse to * event lines by default", () => {
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
    expect(plain).toContain("   * thinking  4.1k tok 6.2s")
    expect(plain).toContain(`   * memory.recall("identity module", 5)  ok 41ms`)
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
    expect(plain).toContain("   * thinking  5 tok")
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
    expect(plain.some((line) => line.includes("+- plan · 1 шаг "))).toBe(true)
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
    expect(plain.some((line) => line.includes("thinking"))).toBe(true)
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
        lines: [paint("x".repeat(30), colors.error)],
      },
    ]
    const lines = flattenTranscript(snapshot({ blocks }), 10, 0)
    expect(lines.length).toBe(3)
    for (const line of lines) {
      expect(stripAnsi(line).length).toBeLessThanOrEqual(10)
    }
    // The color state carries into every continuation chunk.
    expect(lines.every((line) => line.includes(colors.error))).toBe(true)
  })
})

describe("findMatches", () => {
  const lines = [
    paint("hello world", colors.text),
    "plain HELLO again",
    "",
    paint("no match here", colors.error),
    "say hello",
  ]

  test("case-insensitive substring over ANSI-stripped lines", () => {
    expect(findMatches(lines, "hello")).toEqual([0, 1, 4])
    expect(findMatches(lines, "HELLO")).toEqual([0, 1, 4])
  })

  test("blank or whitespace query matches nothing", () => {
    expect(findMatches(lines, "")).toEqual([])
    expect(findMatches(lines, "   ")).toEqual([])
  })

  test("a needle that is nowhere returns empty", () => {
    expect(findMatches(lines, "absent")).toEqual([])
  })

  test("empty transcript matches nothing", () => {
    expect(findMatches([], "x")).toEqual([])
  })
})

describe("nextMatchIndex", () => {
  test("cycles forward and wraps to zero", () => {
    expect(nextMatchIndex(0, 3)).toBe(1)
    expect(nextMatchIndex(1, 3)).toBe(2)
    expect(nextMatchIndex(2, 3)).toBe(0)
  })

  test("cycles backwards and wraps to the last", () => {
    expect(nextMatchIndex(0, 3, -1)).toBe(2)
    expect(nextMatchIndex(2, 3, -1)).toBe(1)
  })

  test("no matches — stays at zero", () => {
    expect(nextMatchIndex(5, 0)).toBe(0)
    expect(nextMatchIndex(5, 0, -1)).toBe(0)
  })

  test("single match cycles in place", () => {
    expect(nextMatchIndex(0, 1)).toBe(0)
  })
})

describe("highlightLine", () => {
  test("wraps every occurrence in inverse video", () => {
    const line = highlightLine("say hello, hello!", "hello")
    expect(line).toContain("\x1b[7m")
    expect(line).toContain("\x1b[27m")
    expect(stripAnsi(line)).toBe("say hello, hello!")
    // Two non-overlapping spans → two on/off pairs.
    expect(line.split("\x1b[7m").length - 1).toBe(2)
  })

  test("the active match adds the underline on top", () => {
    const line = highlightLine("say hello", "hello", true)
    expect(line).toContain(colors.underline)
    expect(line).toContain("\x1b[27m")
    expect(line).toContain("\x1b[24m")
  })

  test("paint inside the line survives around the wrap", () => {
    const painted = `${colors.error}before hello after${colors.reset}`
    const line = highlightLine(painted, "hello")
    expect(line.startsWith(colors.error)).toBe(true)
    expect(line).toContain(colors.reset)
    expect(stripAnsi(line)).toBe("before hello after")
  })

  test("a match inside an ANSI run highlights only the plain chars", () => {
    const painted = paint("abc", colors.error) + paint("def", colors.ok)
    const line = highlightLine(painted, "cdef")
    expect(stripAnsi(line)).toBe("abcdef")
    // Both surrounding colors are still present.
    expect(line).toContain(colors.error)
    expect(line).toContain(colors.ok)
    expect(line).toContain("\x1b[7m")
  })

  test("a match at the very end closes its span", () => {
    const line = highlightLine("tail match", "match")
    expect(line.endsWith("\x1b[27m")).toBe(true)
  })

  test("blank query and absent needle return the line untouched", () => {
    expect(highlightLine("text", "")).toBe("text")
    expect(highlightLine("text", "   ")).toBe("text")
    expect(highlightLine("text", "zzz")).toBe("text")
  })

  test("case-insensitive match, whitespace query trimmed", () => {
    const line = highlightLine("Find ME", " me ")
    expect(line).toContain("\x1b[7m")
    expect(stripAnsi(line)).toBe("Find ME")
  })
})

describe("classifyLine", () => {
  test("reads the visible prefix after ANSI strip", () => {
    expect(classifyLine("")).toBe("blank")
    expect(classifyLine("   ")).toBe("blank")
    expect(classifyLine("> hello")).toBe("user")
    expect(classifyLine("  > hello")).toBe("user")
    expect(classifyLine("* thinking")).toBe("event")
    expect(classifyLine("  * memory.recall")).toBe("event")
    expect(classifyLine("| thinking")).toBe("pulse")
    expect(classifyLine("\\ thinking")).toBe("pulse")
    expect(classifyLine("     expanded reasoning")).toBe("event")
    expect(classifyLine("- deleted")).toBe("rule")
    expect(classifyLine("+ added")).toBe("rule")
    expect(classifyLine("comuki")).toBe("assistant")
    expect(classifyLine("done")).toBe("assistant")
  })
})

describe("padVisible", () => {
  test("right-pads to width on the visible length", () => {
    expect(padVisible("hi", 5)).toBe("hi   ")
    expect(stripAnsi(padVisible(paint("hi", colors.accent), 5)).length).toBe(5)
    expect(padVisible("> hello", 20).length).toBe(20)
    expect(stripAnsi(padVisible("> hello", 20)).length).toBe(20)
    expect(padVisible("already-wide", 4)).toBe("already-wide")
  })
})
