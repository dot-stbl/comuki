import { describe, expect, it } from "bun:test"
import {
  ageFromIso,
  ageFromMs,
  renderMessage,
  renderPart,
  renderPendingPlan,
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

  it("passes markdown text through unchanged", () => {
    expect(renderPart({ kind: "text", markdown: "План:\n1. Шаг" })).toEqual([
      "План:",
      "1. Шаг",
    ])
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
