/**
 * Component-level tests for markdown rendering: the assistant text
 * part flows through `lib/markdown.ts` inside the real Ink tree, user
 * rows stay plain. ink-testing-library's fake stdout disables Ink
 * color transforms, but our raw ANSI strings pass through verbatim —
 * so content assertions run on stripped frames, style assertions on
 * the raw one.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { ChatMessage } from "./ChatMessage"
import type { ChatMessageView, MessagePart } from "../lib/client"
import { stripAnsi } from "../theme"

function assistantMarkdown(markdown: string): ChatMessageView {
  return {
    id: "a1",
    role: "assistant",
    content: markdown,
    toolName: null,
    parts: [{ kind: "text", markdown }],
    meta: null,
    createdAt: "2026-09-18T00:00:00Z",
  }
}

describe("ChatMessage — markdown rendering", () => {
  test("renders a fenced code block with border, label and body", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage
        message={assistantMarkdown("```ts\nconst answer = 42\n```")}
        width={60}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("┌─ ts")
    expect(frame).toContain("│ const answer = 42")
    expect(frame).toContain("└")
    unmount()
  })

  test("renders bold text with the bright style", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage message={assistantMarkdown("this is **important**")} width={60} />
    )
    // Ink re-emits resets as fine-grained off-codes — assert the opener only
    expect(lastFrame()).toContain("\x1b[1mimportant")
    expect(stripAnsi(lastFrame() ?? "")).toContain("this is important")
    unmount()
  })

  test("renders unordered lists with bullets", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage
        message={assistantMarkdown("- alpha\n- beta")}
        width={60}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("· alpha")
    expect(frame).toContain("· beta")
    unmount()
  })

  test("highlights inline code in the terracotta accent", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage message={assistantMarkdown("run `bun test`")} width={60} />
    )
    // Ink re-emits resets as fine-grained off-codes — assert the opener only
    expect(lastFrame()).toContain("\x1b[38;5;173mbun test")
    unmount()
  })

  test("keeps user messages as bare bold text with no prefix glyph", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage
        message={{
          id: "u1",
          role: "user",
          content: "сделай план",
          toolName: null,
          parts: null,
          meta: null,
          createdAt: "2026-09-18T00:00:00Z",
        }}
        width={60}
      />
    )
    const frame = lastFrame() ?? ""
    // no markdown machinery for user rows: no border, no cursor
    expect(frame).not.toContain("│")
    expect(frame).not.toContain("▌")
    // one blank line before AND after (ink trims trailing blanks in the
    // captured frame), the echo itself bold at column 0 — no › prefix
    expect(frame.split("\n")[0]).toBe("")
    expect(frame).toContain("сделай план")
    expect(frame).toContain("\x1b[1mсделай план")
    expect(stripAnsi(frame)).not.toContain("›")
    unmount()
  })

  test("leads assistant messages with the brand glyph and comuki label", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage
        message={assistantMarkdown("here is the plan")}
        width={60}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame.split("\n")[0]).toBe(" ◆ comuki")
    expect(frame).toContain(" here is the plan")
    unmount()
  })

  test("wraps long assistant prose to the given width", () => {
    const words = Array.from({ length: 40 }, (_, i) => `w${i}`).join(" ")
    const { lastFrame, unmount } = render(
      <ChatMessage message={assistantMarkdown(words)} width={40} />
    )
    const lines = (lastFrame() ?? "").split("\n")
    expect(lines.length).toBeGreaterThan(1)
    for (const line of lines) {
      expect(stripAnsi(line).length).toBeLessThanOrEqual(40)
    }
    unmount()
  })
})

describe("ChatMessage — collapsible parts", () => {
  function assistantWithParts(parts: MessagePart[]): ChatMessageView {
    return {
      id: "a2",
      role: "assistant",
      content: "",
      toolName: null,
      parts,
      meta: null,
      createdAt: "2026-09-18T00:00:00Z",
    }
  }

  test("collapses thinking and tool parts by default", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage
        message={assistantWithParts([
          { kind: "thinking", text: "hidden reasoning", tokens: 40 },
          {
            kind: "tool",
            name: "memory.recall",
            inputJson: `{"query":"identity"}`,
            status: "succeeded",
            outputJson: `{"facts":[1]}`,
          },
          { kind: "text", markdown: "the visible answer" },
        ])}
        width={60}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("⏺ thinking · 40 tok")
    expect(frame).toContain(`⏺ memory.recall("identity")  ok`)
    expect(frame).toContain("the visible answer")
    expect(frame).not.toContain("hidden reasoning")
    unmount()
  })

  test("expanded prop reveals full blocks", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage
        message={assistantWithParts([
          { kind: "thinking", text: "revealed reasoning" },
          {
            kind: "tool",
            name: "memory.recall",
            inputJson: `{"query":"identity"}`,
            status: "succeeded",
            outputJson: `{"facts":[1]}`,
          },
        ])}
        width={60}
        expanded={true}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("revealed reasoning")
    expect(frame).toContain(`"query": "identity"`)
    expect(frame).toContain(`"facts": [`)
    unmount()
  })
})
