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
import type { ChatMessageView } from "../lib/client"
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

  test("renders inline code highlighted in the accent color", () => {
    const { lastFrame, unmount } = render(
      <ChatMessage message={assistantMarkdown("run `bun test`")} width={60} />
    )
    // Ink re-emits resets as fine-grained off-codes — assert the opener only
    expect(lastFrame()).toContain("\x1b[38;5;104mbun test")
    unmount()
  })

  test("keeps user messages plain with the dim mark and gutter", () => {
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
    // one blank line before (turn separator; ink trims its spaces in
    // the captured frame), then the dim-marked echo
    expect(frame.split("\n")[0]).toBe("")
    expect(frame.split("\n").length).toBe(2)
    expect(stripAnsi(frame)).toContain(" › сделай план")
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
