import { describe, expect, it } from "bun:test"
import {
  ageFromIso,
  ageFromMs,
  gutterLines,
  normalizeSpacing,
  renderMessage,
  renderPart,
  renderPendingPlan,
  renderUserEcho,
  summarizeToolInput,
  summarizeToolOutput,
  renderToolPart,
} from "./format"
import { colors, stripAnsi, symbols } from "../theme"
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

describe("summarizeToolInput", () => {
  it("renders string arguments quoted", () => {
    expect(summarizeToolInput("memory.search", `{"query":"identity"}`)).toBe(
      `memory.search("identity")`
    )
  })

  it("keeps at most two arguments and truncates long strings", () => {
    expect(
      summarizeToolInput("t", `{"a":"${"x".repeat(50)}","b":1,"c":2}`)
    ).toBe(`t("${"x".repeat(29)}…", 1)`)
  })

  it("counts array arguments", () => {
    expect(summarizeToolInput("x", `{"keys":[1,2,3,4]}`)).toBe("x(4 keys)")
  })

  it("falls back to the bare name on broken json", () => {
    expect(summarizeToolInput("x", "not json")).toBe("x")
  })
})

describe("summarizeToolOutput", () => {
  it("counts array outputs", () => {
    expect(summarizeToolOutput(`[{"f":1},{"f":2}]`, "success")).toBe("2 items")
  })

  it("prefers count/total fields", () => {
    expect(summarizeToolOutput(`{"total":12}`, "success")).toBe("12")
  })

  it("counts named array fields as facts", () => {
    expect(summarizeToolOutput(`{"facts":[{},{},{}]}`, "success")).toBe(
      "3 facts"
    )
  })

  it("stays quiet while running or empty", () => {
    expect(summarizeToolOutput(null, "running")).toBe("")
    expect(summarizeToolOutput(null, "success")).toBe("")
  })
})

describe("renderToolPart", () => {
  it("marks success with a checkmark and muted call", () => {
    const line = renderToolPart({
      kind: "tool",
      name: "memory.search",
      inputJson: `{"query":"identity"}`,
      status: "success",
      outputJson: `{"facts":[{},{}]}`,
      durationMs: 1200,
    })
    expect(stripAnsi(line)).toContain(
      '✓ memory.search("identity")  2 facts · 1200ms'
    )
  })

  it("marks failure red", () => {
    const line = renderToolPart({
      kind: "tool",
      name: "boom",
      inputJson: "{}",
      status: "failed",
    })
    expect(line).toContain(colors.red)
    expect(stripAnsi(line)).toContain("✗ boom")
  })
})

describe("renderPart", () => {
  it("dims thinking lines and indents them", () => {
    const lines = renderPart({
      kind: "thinking",
      text: "considering\nthe identity module",
      tokens: 40,
    })
    expect(lines).toHaveLength(2)
    expect(lines[0]).toContain(colors.dim)
    expect(stripAnsi(lines[0] ?? "")).toBe("  considering")
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
})

describe("renderPendingPlan", () => {
  it("renders nodes from raw plan json", () => {
    const lines = renderPendingPlan({
      nodes: [
        { key: "n1", profileKey: "implement", brief: "do it", dependsOn: [] },
      ],
    })
    expect(stripAnsi(lines[0] ?? "")).toContain("implement → do it")
  })

  it("degrades to a dim note on unreadable plans", () => {
    const lines = renderPendingPlan("garbage")
    expect(lines[0]).toContain(colors.dim)
  })
})

describe("renderMessage", () => {
  it("echoes user rows with the dim mark, bright text and a leading blank", () => {
    const lines = renderMessage(userMessage("сделай план"))
    expect(lines[0]).toBe("")
    const line = stripAnsi(lines[1] ?? "")
    expect(line).toBe(` ${symbols.prompt} сделай план`)
    expect(lines[1]).toContain(colors.dim)
    expect(lines[1]).toContain(colors.bright)
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
