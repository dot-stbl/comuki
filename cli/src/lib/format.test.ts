import { describe, expect, it } from "bun:test"
import {
  ageFromIso,
  ageFromMs,
  collapsedSummary,
  formatDurationMs,
  formatTokenCount,
  renderMessage,
  renderPart,
  renderPendingPlan,
  summarizeToolArgs,
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
  it("echoes user rows with the accent prompt", () => {
    const [line] = renderMessage(userMessage("сделай план"))
    expect(line).toContain(colors.accent)
    expect(stripAnsi(line ?? "")).toBe(`you  ${symbols.prompt} сделай план`)
  })

  it("falls back to content when parts are absent", () => {
    const lines = renderMessage({
      ...assistantMessage([{ kind: "text", markdown: "" }]),
      parts: null,
      content: "plain reply",
    })
    expect(lines).toEqual(["plain reply"])
  })

  it("mutes tool journal rows", () => {
    const lines = renderMessage({
      ...userMessage("done"),
      role: "tool",
      toolName: "create_ticket",
      content: "ticket COM-1",
    })
    expect(stripAnsi(lines[0] ?? "")).toContain("create_ticket: ticket COM-1")
    expect(lines[0]).toContain(colors.muted)
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
  it("derives a thinking summary with duration when known", () => {
    expect(
      collapsedSummary({
        kind: "thinking",
        text: "reasoning here",
        tokens: 40,
        durationMs: 3_400,
      })
    ).toEqual({ icon: symbols.thinking, label: "thinking", badge: "3.4s" })
  })

  it("falls back to tokens, then to a bare badge for thinking", () => {
    expect(
      collapsedSummary({ kind: "thinking", text: "a", tokens: 1_240 })?.badge
    ).toBe("1.2k tok")
    expect(
      collapsedSummary({ kind: "thinking", text: "a", tokens: null })?.badge
    ).toBeNull()
  })

  it("derives a tool summary with name, args and status badge", () => {
    expect(
      collapsedSummary({
        kind: "tool",
        name: "memory.recall",
        inputJson: `{"q":"ids"}`,
        status: "succeeded",
        outputJson: "[]",
      })
    ).toEqual({
      icon: symbols.tool,
      label: `memory.recall("ids")`,
      badge: "ok",
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
  it("collapses thinking to one dim summary line", () => {
    const lines = renderPart(
      { kind: "thinking", text: "long\nreasoning", tokens: 40 },
      80,
      { expanded: false }
    )
    expect(lines).toHaveLength(1)
    expect(lines[0]).toContain(colors.dim)
    expect(stripAnsi(lines[0] ?? "")).toBe("  ◌ thinking · 40 tok")
  })

  it("collapses a tool call to name(args) with a result badge", () => {
    const lines = renderPart(
      {
        kind: "tool",
        name: "memory.recall",
        inputJson: `{"q":"ids"}`,
        status: "succeeded",
      },
      80,
      { expanded: false }
    )
    expect(lines).toHaveLength(1)
    expect(stripAnsi(lines[0] ?? "")).toBe(`  ⚙ memory.recall("ids") → ok`)
  })

  it("paints the failed badge red and the running badge accent", () => {
    const failed = renderPart(
      { kind: "tool", name: "boom", inputJson: "{}", status: "failed" },
      80,
      { expanded: false }
    )
    expect(failed[0]).toContain(colors.red)
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
    expect(lines).toHaveLength(1)
    expect(stripAnsi(lines[0] ?? "")).toBe("  considering")
  })

  it("expands thinking through the markdown renderer, dimmed", () => {
    const lines = renderPart(
      { kind: "thinking", text: "hmm **why** not", tokens: 1 },
      80,
      { expanded: true }
    )
    expect(stripAnsi(lines.join("\n"))).toContain("hmm why not")
    expect(lines[0]).toContain(colors.dim)
  })

  it("expands a tool with pretty-printed input and output blocks", () => {
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
    expect(frame).toContain(`memory.recall("ids")`)
    expect(frame).toContain("input:")
    expect(frame).toContain(`"q": "ids"`)
    expect(frame).toContain("output:")
    expect(frame).toContain('"facts": [')
  })
})

describe("renderMessage — collapsed transcript", () => {
  it("hides thinking behind the summary line, keeps the answer", () => {
    const lines = renderMessage(
      assistantMessage([
        { kind: "thinking", text: "secret reasoning", tokens: 10 },
        { kind: "text", markdown: "the answer" },
      ]),
      80,
      { expanded: false }
    )
    const frame = stripAnsi(lines.join("\n"))
    expect(frame).toContain("◌ thinking · 10 tok")
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
