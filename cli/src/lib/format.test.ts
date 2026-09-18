import { describe, expect, it } from "bun:test"
import {
  ageFromIso,
  ageFromMs,
  collapsedSummary,
  formatDurationMs,
  formatTokenCount,
  gutterLines,
  normalizeSpacing,
  renderMessage,
  renderPart,
  renderPendingPlan,
  renderUserEcho,
  summarizeToolArgs,
} from "./format"
import { colors, messageMark, stripAnsi, symbols } from "../theme"
import type { ChatMessageView, MessagePart } from "./client"

function userMessage(content: string): ChatMessageView {
  return {
    id: "u1",
    role: "user",
    content,
    toolName: null,
    parts: null,
    meta: null,
    createdAt: "2026-09-17T00:00:00Z",
  }
}

function assistantMessage(parts: MessagePart[]): ChatMessageView {
  return {
    id: "a1",
    role: "assistant",
    content: parts
      .map((part) => (part.kind === "text" ? part.markdown : ""))
      .join(""),
    toolName: null,
    parts,
    meta: null,
    createdAt: "2026-09-17T00:00:00Z",
  }
}

describe("renderPendingPlan", () => {
  it("frames the plan card with numbered steps and the colored hint", () => {
    const lines = renderPendingPlan({
      nodes: [
        { key: "n1", profileKey: "implement", brief: "do it", dependsOn: [] },
        { key: "n2", profileKey: "review", brief: "check it", dependsOn: [] },
      ],
    })
    const plain = lines.map(stripAnsi)
    expect(plain[0]).toContain("┌─ plan · 2 шага ")
    expect(plain[0]?.endsWith("┐")).toBe(true)
    expect(plain[1]).toContain("│ 1 · do it")
    expect(plain[2]).toContain("│ 2 · check it")
    expect(plain[3]).toMatch(/^  └─+┘$/)
    expect(plain[4]).toContain("approve · reject [reason]")
    // The frame draws in rule; approve lavender, reject yellow — the
    // words carry the status, colour only echoes it.
    expect(lines[0]).toContain(colors.rule)
    expect(lines[4]).toContain(colors.ok)
    expect(lines[4]).toContain(colors.error)
    // The frame is one closed box: every row shares its visible width.
    const widths = plain.slice(0, 4).map((line) => line.length)
    expect(new Set(widths).size).toBe(1)
  })

  it("reads the canonical wire nodes (id + title) too", () => {
    const lines = renderPendingPlan({
      nodes: [
        { id: "n1", title: "wire step", profileKey: "implement", brief: "" },
      ],
    })
    expect(stripAnsi(lines[1] ?? "")).toContain("│ 1 · wire step")
  })

  it("russianizes the step count in the header", () => {
    const one = renderPendingPlan({
      nodes: [{ key: "a", profileKey: "x", brief: "b", dependsOn: [] }],
    })
    expect(stripAnsi(one[0] ?? "")).toContain("plan · 1 шаг ")
    const five = renderPendingPlan({
      nodes: [1, 2, 3, 4, 5].map((n) => ({
        key: `k${n}`,
        profileKey: "x",
        brief: "b",
        dependsOn: [],
      })),
    })
    expect(stripAnsi(five[0] ?? "")).toContain("plan · 5 шагов ")
  })

  it("cites the estimate only when the payload carries one", () => {
    const withEstimate = renderPendingPlan({
      estimateMinutes: 40,
      nodes: [{ key: "a", profileKey: "x", brief: "b", dependsOn: [] }],
    })
    expect(stripAnsi(withEstimate[0] ?? "")).toContain("est 40m")
    const without = renderPendingPlan({
      nodes: [{ key: "a", profileKey: "x", brief: "b", dependsOn: [] }],
    })
    expect(stripAnsi(without[0] ?? "")).not.toContain("est")
  })

  it("keeps the frame inside the terminal width", () => {
    const lines = renderPendingPlan(
      {
        nodes: [
          {
            key: "n1",
            profileKey: "implement",
            brief: "x".repeat(200),
            dependsOn: [],
          },
        ],
      },
      60
    )
    for (const line of lines.slice(0, 4)) {
      expect(stripAnsi(line).length).toBeLessThanOrEqual(60)
    }
  })

  it("degrades to a dim note on unreadable plans", () => {
    const lines = renderPendingPlan("garbage")
    expect(lines[0]).toContain(colors.dim)
  })
})

describe("renderMessage", () => {
  it("echoes user rows as bare bold text with a blank line on each side", () => {
    const lines = renderMessage(userMessage("сделай план"))
    expect(lines).toHaveLength(3)
    expect(lines[0]).toBe("")
    expect(lines[2]).toBe("")
    expect(stripAnsi(lines[1] ?? "")).toBe("сделай план")
    expect(lines[1]).toContain(colors.bright)
    expect(lines[1]).toContain(colors.text)
    expect(lines[1]).not.toContain("›")
  })

  it("leads assistant rows with the brand glyph and dim comuki label", () => {
    const lines = renderMessage(assistantMessage([
      { kind: "text", markdown: "done" },
    ]))
    const header = stripAnsi(lines[0] ?? "")
    expect(header).toBe(` ${symbols.brandMark} comuki`)
    expect(lines[0]).toContain(colors.accent)
    expect(lines[0]).toContain(colors.dim)
    expect(stripAnsi(lines[1] ?? "")).toBe(" done")
  })

  it("breathes one blank line between the events block and the answer", () => {
    const lines = renderMessage(
      assistantMessage([
        { kind: "thinking", text: "hidden", tokens: 40 },
        { kind: "tool", name: "x", inputJson: "{}", status: "succeeded" },
        { kind: "text", markdown: "the answer" },
      ]),
      80,
      { expanded: false }
    )
    const plain = lines.map(stripAnsi)
    const eventIndex = plain.findIndex((line) => line.includes("thinking"))
    const answerIndex = plain.findIndex((line) =>
      line.includes("the answer")
    )
    expect(plain[eventIndex + 1]).toContain("x()")
    expect(plain[answerIndex - 1]).toBe("")
  })

  it("gutters every non-empty line of an assistant row", () => {
    const lines = renderMessage(assistantMessage([
      { kind: "text", markdown: "first\n\nsecond" },
    ]))
    for (const line of lines) {
      expect(line === "" || line.startsWith(" ")).toBe(true)
    }
    expect(lines[lines.length - 1]).not.toBe("")
  })

  it("falls back to content when parts are absent", () => {
    const lines = renderMessage({
      ...assistantMessage([{ kind: "text", markdown: "" }]),
      parts: null,
      content: "plain reply",
    })
    expect(stripAnsi(lines[0] ?? "")).toBe(` ${symbols.brandMark} comuki`)
    expect(stripAnsi(lines[1] ?? "")).toBe(" plain reply")
  })

  it("mutes tool journal rows on the shared gutter", () => {
    const lines = renderMessage({
      ...userMessage("done"),
      role: "tool",
      toolName: "create_ticket",
      content: "ticket COM-1",
    })
    expect(stripAnsi(lines[0] ?? "")).toContain(
      " · create_ticket: ticket COM-1"
    )
    expect(lines[0]).toContain(colors.muted)
  })
})

describe("renderUserEcho", () => {
  it("is byte-identical to the history rendering of the same text", () => {
    const echo = renderUserEcho("привет")
    const history = renderMessage(userMessage("привет"))
    expect(echo).toEqual(history)
  })

  it("renders nothing for blank input", () => {
    expect(renderUserEcho("   ")).toEqual([])
  })

  it("accents @mention tokens inside the bold text", () => {
    const echo = renderUserEcho("use @identity here")
    const line = echo[1] ?? ""
    expect(line).toContain(colors.accent + "@identity")
    expect(line).toContain(messageMark("user").textColor + "use ")
    // Byte-identity holds for mention lines too (echo vs history).
    expect(echo).toEqual(renderMessage(userMessage("use @identity here")))
  })

  it("hides the [@knowledge: …] preamble of a stored message", () => {
    const stored =
      "use @identity please\n\n[@knowledge: Identity — the chunk text]"
    const echo = renderUserEcho(stored)
    expect(echo[1] ?? "").not.toContain("[@knowledge:")
    expect(stripAnsi(echo[1] ?? "")).toBe("use @identity please")
  })
})

describe("normalizeSpacing", () => {
  it("collapses blank runs to one and trims trailing blanks", () => {
    expect(
      normalizeSpacing(["", "", "a", "", "", "", "b", "", ""])
    ).toEqual(["", "a", "", "b"])
  })

  it("keeps single blanks and treats whitespace-only lines as blank", () => {
    expect(normalizeSpacing(["a", "   ", "b"])).toEqual(["a", "", "b"])
    expect(normalizeSpacing(["a", "", "b"])).toEqual(["a", "", "b"])
  })

  it("returns an empty list for all-blank input", () => {
    expect(normalizeSpacing(["", "  ", ""])).toEqual([])
  })
})

describe("gutterLines", () => {
  it("prefixes non-empty lines only", () => {
    expect(gutterLines(["a", "", "b"])).toEqual([" a", "", " b"])
  })
})

describe("ageFromMs / ageFromIso", () => {
  it("formats coarse ages", () => {
    expect(ageFromMs(5_000)).toBe("5s")
    expect(ageFromMs(120_000)).toBe("2m")
    expect(ageFromMs(7_200_000)).toBe("2h")
    expect(ageFromMs(172_800_000)).toBe("2d")
  })

  it("parses iso timestamps relative to now", () => {
    const now = new Date("2026-09-17T12:00:00Z")
    expect(ageFromIso("2026-09-17T11:59:30Z", now)).toBe("30s")
    expect(ageFromIso("nope", now)).toBe("?")
  })
})

describe("formatDurationMs / formatTokenCount", () => {
  it("formats compact durations for collapsed lines", () => {
    expect(formatDurationMs(120)).toBe("120ms")
    expect(formatDurationMs(3_400)).toBe("3.4s")
    expect(formatDurationMs(125_000)).toBe("2m 5s")
    expect(formatDurationMs(180_000)).toBe("3m")
  })

  it("formats token counts with a k suffix past a thousand", () => {
    expect(formatTokenCount(40)).toBe("40 tok")
    expect(formatTokenCount(1_240)).toBe("1.2k tok")
  })
})

describe("summarizeToolArgs", () => {
  it("joins the first arguments inside the 40-char budget", () => {
    expect(summarizeToolArgs(`{"query":"identity","limit":5}`)).toBe(
      `"identity", 5`
    )
  })

  it("truncates a long value with an ellipsis at the budget", () => {
    expect(summarizeToolArgs(`{"q":"${"x".repeat(60)}"}`)).toBe(
      `"${"x".repeat(38)}…`
    )
  })

  it("counts array arguments and skips nested objects", () => {
    expect(summarizeToolArgs(`{"keys":[1,2],"filter":{"deep":1}}`)).toBe(
      "2 keys"
    )
  })

  it("returns empty for broken json", () => {
    expect(summarizeToolArgs("not json")).toBe("")
  })
})

describe("collapsedSummary", () => {
  it("derives a thinking summary with tokens and duration when known", () => {
    expect(
      collapsedSummary({
        kind: "thinking",
        text: "reasoning here",
        tokens: 4_100,
        durationMs: 6_200,
      })
    ).toEqual({
      icon: symbols.event,
      label: "thinking",
      badge: null,
      details: ["4.1k tok", "6.2s"],
    })
  })

  it("omits what the wire did not carry — never invents numbers", () => {
    expect(
      collapsedSummary({ kind: "thinking", text: "a", tokens: null })
    ).toEqual({ icon: symbols.event, label: "thinking", badge: null, details: [] })
    expect(
      collapsedSummary({
        kind: "tool",
        name: "x",
        inputJson: "{}",
        status: "succeeded",
      })
    ).toEqual({
      icon: symbols.event,
      label: "x()",
      badge: "ok",
      details: [],
    })
  })

  it("derives a tool summary with name, args, status badge and duration", () => {
    expect(
      collapsedSummary({
        kind: "tool",
        name: "memory.recall",
        inputJson: `{"q":"ids"}`,
        status: "succeeded",
        outputJson: "[]",
        durationMs: 41,
      })
    ).toEqual({
      icon: symbols.event,
      label: `memory.recall("ids")`,
      badge: "ok",
      details: ["41ms"],
    })
    expect(
      collapsedSummary({
        kind: "tool",
        name: "x",
        inputJson: "{}",
        status: "running",
      })?.badge
    ).toBe("…")
    expect(
      collapsedSummary({
        kind: "tool",
        name: "x",
        inputJson: "{}",
        status: "failed",
      })?.badge
    ).toBe("error")
  })

  it("returns null for non-collapsible parts", () => {
    expect(collapsedSummary({ kind: "text", markdown: "hi" })).toBeNull()
    expect(
      collapsedSummary({ kind: "code", language: "ts", source: "1" })
    ).toBeNull()
    expect(collapsedSummary({ kind: "diagram", dialect: "mmd", source: "x" })).toBeNull()
    expect(collapsedSummary({ kind: "handoff", query: "q" })).toBeNull()
    expect(collapsedSummary({ kind: "plan", nodes: [], edges: [] })).toBeNull()
  })
})

describe("renderPart — collapsible blocks", () => {
  it("collapses thinking to one dim ⏺ event line", () => {
    const lines = renderPart(
      { kind: "thinking", text: "long\nreasoning", tokens: 40 },
      80,
      { expanded: false }
    )
    expect(lines).toHaveLength(1)
    expect(lines[0]).toContain(colors.dim)
    expect(stripAnsi(lines[0] ?? "")).toBe("  ⏺ thinking · 40 tok")
  })

  it("collapses a tool call to name(args) with the status two spaces after", () => {
    const lines = renderPart(
      {
        kind: "tool",
        name: "memory.recall",
        inputJson: `{"q":"ids"}`,
        status: "succeeded",
        durationMs: 41,
      },
      80,
      { expanded: false }
    )
    expect(lines).toHaveLength(1)
    expect(stripAnsi(lines[0] ?? "")).toBe(
      `  ⏺ memory.recall("ids")  ok 41ms`
    )
  })

  it("paints the failed badge yellow and the running badge accent", () => {
    const failed = renderPart(
      { kind: "tool", name: "boom", inputJson: "{}", status: "failed" },
      80,
      { expanded: false }
    )
    expect(failed[0]).toContain(colors.error)
    const running = renderPart(
      { kind: "tool", name: "boom", inputJson: "{}", status: "running" },
      80,
      { expanded: false }
    )
    expect(running[0]).toContain(colors.accent)
  })

  it("keeps the full rendering when options are omitted", () => {
    const lines = renderPart({
      kind: "thinking",
      text: "considering",
      tokens: 1,
    })
    expect(stripAnsi(lines[0] ?? "")).toBe("  ⏺ thinking · 1 tok")
    expect(stripAnsi(lines[1] ?? "")).toBe("    considering")
  })

  it("expands thinking under the event line, dimmed and indented", () => {
    const lines = renderPart(
      { kind: "thinking", text: "hmm **why** not", tokens: 1 },
      80,
      { expanded: true }
    )
    expect(stripAnsi(lines.join("\n"))).toContain("hmm why not")
    expect(stripAnsi(lines[0] ?? "")).toBe("  ⏺ thinking · 1 tok")
    expect(stripAnsi(lines[1] ?? "")).toBe("    hmm why not")
    expect(lines[1]).toContain(colors.dim)
  })

  it("expands a tool with pretty-printed input and output under the event line", () => {
    const lines = renderPart(
      {
        kind: "tool",
        name: "memory.recall",
        inputJson: `{"q":"ids"}`,
        status: "succeeded",
        outputJson: `{"facts":[1]}`,
        durationMs: 1200,
      },
      80,
      { expanded: true }
    )
    const frame = stripAnsi(lines.join("\n"))
    expect(stripAnsi(lines[0] ?? "")).toBe(
      `  ⏺ memory.recall("ids")  ok 1.2s`
    )
    expect(frame).toContain("input:")
    expect(frame).toContain(`"q": "ids"`)
    expect(frame).toContain("output:")
    expect(frame).toContain('"facts": [')
  })
})

describe("renderPart — full blocks", () => {
  it("dims expanded thinking lines and indents them under the event", () => {
    const lines = renderPart({
      kind: "thinking",
      text: "considering\nthe identity module",
      tokens: 40,
    })
    expect(lines).toHaveLength(3)
    expect(lines[1]).toContain(colors.dim)
    expect(stripAnsi(lines[1] ?? "")).toBe("    considering")
  })

  it("renders plan items as profile → brief bullets", () => {
    const lines = renderPart({
      kind: "plan",
      nodes: [
        {
          key: "a",
          profileKey: "implement",
          brief: "split Identity into Users/Grants/Keys\ndetails",
          dependsOn: [],
        },
        {
          key: "b",
          profileKey: "review",
          brief: "review the split",
          dependsOn: ["a"],
        },
      ],
      edges: [{ from: "a", to: "b" }],
    })
    expect(stripAnsi(lines[0] ?? "")).toContain(
      "implement → split Identity into Users/Grants/Keys"
    )
    expect(stripAnsi(lines[1] ?? "")).toContain("← a")
  })

  it("renders markdown text parts through the markdown path", () => {
    const lines = renderPart({ kind: "text", markdown: "План:\n1. Шаг" })
    expect(lines.map(stripAnsi)).toEqual(["План:", "", "  1. Шаг"])
  })

  it("renders a diff-tagged code part as signed lines, not a muted fence", () => {
    const lines = renderPart(
      {
        kind: "code",
        language: "diff",
        source: "@@ -1,2 +1,2 @@\n-old line\n+new line\n context",
      },
      80
    )
    expect(stripAnsi(lines[0] ?? "")).toBe("  · diff")
    expect(stripAnsi(lines[1] ?? "")).toBe("  @@ -1,2 +1,2 @@")
    expect(lines[1]).toContain(colors.accent)
    expect(stripAnsi(lines[2] ?? "")).toBe("  -old line")
    expect(lines[2]).toContain(colors.error)
    expect(stripAnsi(lines[3] ?? "")).toBe("  +new line")
    expect(lines[3]).toContain(colors.ok)
    expect(lines[4]).toContain(colors.faint)
  })

  it("sniffs an untagged unified diff body and renders it structured", () => {
    const lines = renderPart(
      {
        kind: "code",
        language: "",
        source: "--- a/one.ts\n+++ b/one.ts\n@@ -1 +1 @@\n-a\n+b",
      },
      80
    )
    expect(lines.some((line) => line.includes(colors.ok))).toBe(true)
    expect(lines.some((line) => line.includes(colors.error))).toBe(true)
  })

  it("keeps a plain code fence muted when it is not a diff", () => {
    const lines = renderPart(
      { kind: "code", language: "ts", source: "+plus\n-minus" },
      80
    )
    expect(lines[1]).toContain(colors.muted)
    expect(lines.some((line) => line.includes(colors.ok))).toBe(false)
  })
})

describe("renderMessage — collapsed transcript", () => {
  it("hides thinking behind the ⏺ event line, keeps the answer", () => {
    const lines = renderMessage(
      assistantMessage([
        { kind: "thinking", text: "secret reasoning", tokens: 10 },
        { kind: "text", markdown: "the answer" },
      ]),
      80,
      { expanded: false }
    )
    const frame = stripAnsi(lines.join("\n"))
    expect(frame).toContain("⏺ thinking · 10 tok")
    expect(frame).not.toContain("secret reasoning")
    expect(frame).toContain("the answer")
  })

  it("reveals everything when expanded", () => {
    const lines = renderMessage(
      assistantMessage([{ kind: "thinking", text: "secret reasoning" }]),
      80,
      { expanded: true }
    )
    expect(stripAnsi(lines.join("\n"))).toContain("secret reasoning")
  })
})
