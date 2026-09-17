import { describe, expect, it } from "bun:test"
import { historyNavigator, lastAssistantText } from "./history"
import type { ChatBlock } from "./sessions"
import type { ChatMessageView } from "./client"

function message(id: string, role: string, content: string): ChatMessageView {
  return {
    id,
    role,
    content,
    toolName: null,
    parts: null,
    meta: null,
    createdAt: "2026-01-01T00:00:00Z",
  }
}

function transcript(blocks: readonly ChatBlock[]): readonly ChatBlock[] {
  return blocks
}

describe("historyNavigator", () => {
  it("returns null for empty history in every state", () => {
    expect(historyNavigator(null, "older", [])).toBeNull()
    expect(historyNavigator(null, "newer", [])).toBeNull()
    expect(historyNavigator(0, "older", [])).toBeNull()
    expect(historyNavigator(3, "newer", [])).toBeNull()
  })

  it("↑ from the draft jumps to the newest entry", () => {
    expect(historyNavigator(null, "older", ["a", "b", "c"])).toBe(2)
  })

  it("↑ walks toward older entries and clamps at the oldest", () => {
    expect(historyNavigator(2, "older", ["a", "b", "c"])).toBe(1)
    expect(historyNavigator(1, "older", ["a", "b", "c"])).toBe(0)
    expect(historyNavigator(0, "older", ["a", "b", "c"])).toBe(0)
  })

  it("↓ walks toward newer entries and past newest returns to draft", () => {
    expect(historyNavigator(0, "newer", ["a", "b", "c"])).toBe(1)
    expect(historyNavigator(1, "newer", ["a", "b", "c"])).toBe(2)
    expect(historyNavigator(2, "newer", ["a", "b", "c"])).toBeNull()
  })

  it("↓ on the draft stays on the draft", () => {
    expect(historyNavigator(null, "newer", ["a", "b"])).toBeNull()
  })

  it("clamps a stale out-of-bounds index instead of indexing past the end", () => {
    expect(historyNavigator(9, "older", ["a", "b"])).toBe(0)
    expect(historyNavigator(9, "newer", ["a", "b"])).toBeNull()
    expect(historyNavigator(-4, "older", ["a", "b"])).toBe(0)
  })
})

describe("lastAssistantText", () => {
  it("returns undefined for an empty transcript", () => {
    expect(lastAssistantText(transcript([]))).toBeUndefined()
  })

  it("returns undefined when only user messages exist", () => {
    const blocks = transcript([
      { kind: "message", key: "1", message: message("1", "user", "hello?") },
      { kind: "message", key: "2", message: message("2", "user", "again?") },
    ])
    expect(lastAssistantText(blocks)).toBeUndefined()
  })

  it("returns the newest assistant message text", () => {
    const blocks = transcript([
      { kind: "message", key: "1", message: message("1", "user", "q") },
      { kind: "message", key: "2", message: message("2", "assistant", "old") },
      { kind: "message", key: "3", message: message("3", "assistant", "new") },
    ])
    expect(lastAssistantText(blocks)).toBe("new")
  })

  it("skips non-message blocks", () => {
    const blocks = transcript([
      { kind: "lines", key: "1", lines: ["echo"] },
      { kind: "message", key: "2", message: message("2", "assistant", "ans") },
    ])
    expect(lastAssistantText(blocks)).toBe("ans")
  })
})
